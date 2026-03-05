from __future__ import annotations

from datetime import datetime, timedelta, timezone
from typing import Any
from uuid import uuid4

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, Field

from main import limiter

from .db import client, governance_sessions, governance_votes, maps_collection, users_collection
from .economy_rules import credit_bank_dividend
from .staking_rules import normalize_staked_map_ids, vote_weight_multiplier
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    build_owned_maps_sync_update,
    build_user_defaults_update,
    serialize_session_user,
)

router = APIRouter(prefix="/database/governance")


class GovernanceStartPayload(BaseModel):
    username: str
    title: str = Field(min_length=3, max_length=120)
    description: str = Field(default="", max_length=500)
    options: list[str] = Field(min_length=2, max_length=6)
    duration_hours: int = Field(default=24, ge=1, le=168)


class GovernanceClosePayload(BaseModel):
    username: str


class GovernanceVotePayload(BaseModel):
    username: str
    option_id: str
    mn_spent: int = Field(ge=1, le=1_000_000)


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _parse_utc_datetime(value: Any) -> datetime | None:
    if isinstance(value, datetime):
        parsed = value
    elif isinstance(value, str):
        trimmed = value.strip()
        if not trimmed:
            return None
        try:
            parsed = datetime.fromisoformat(trimmed.replace("Z", "+00:00"))
        except ValueError:
            return None
    else:
        return None

    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def _is_bank_user(username: str) -> bool:
    return username.strip().lower() == SYSTEM_BANK_USERNAME


def _sync_user(username: str) -> dict[str, Any]:
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    update_query: dict[str, Any] = {}
    set_updates = build_user_defaults_update(user)
    if set_updates:
        update_query["$set"] = set_updates

    owned_to_add, owned_to_remove = build_owned_maps_sync_update(user, maps_collection)
    if owned_to_add:
        update_query.setdefault("$addToSet", {})["maps_owned"] = {"$each": owned_to_add}
    if owned_to_remove:
        update_query.setdefault("$pull", {})["maps_owned"] = {"$in": owned_to_remove}

    if update_query:
        users_collection.update_one({"_id": user["_id"]}, update_query)
        user = users_collection.find_one({"_id": user["_id"]}) or user

    return user


def _normalize_options(options: list[str]) -> list[str]:
    normalized: list[str] = []
    dedupe: set[str] = set()
    for option in options:
        label = str(option or "").strip()
        if not label:
            continue
        key = label.casefold()
        if key in dedupe:
            continue
        dedupe.add(key)
        normalized.append(label[:80])
    if len(normalized) < 2:
        raise HTTPException(status_code=400, detail="At least two unique options are required")
    return normalized


def _get_active_session() -> dict[str, Any] | None:
    active = governance_sessions.find_one(
        {"status": "active"},
        sort=[("started_at", -1)],
    )
    if not active:
        return None

    ends_at = _parse_utc_datetime(active.get("ends_at"))
    now = _utc_now()
    if ends_at and ends_at <= now:
        governance_sessions.update_one(
            {"_id": active["_id"], "status": "active"},
            {"$set": {"status": "closed", "closed_at": now, "updated_at": now}},
        )
        return None

    return active


def _serialize_session(
    session_doc: dict[str, Any] | None,
    user: dict[str, Any] | None = None,
) -> dict[str, Any] | None:
    if not session_doc:
        return None

    tallies = session_doc.get("tallies", {}) if isinstance(session_doc.get("tallies"), dict) else {}
    options_payload: list[dict[str, Any]] = []
    for option in list(session_doc.get("options", []) or []):
        if not isinstance(option, dict):
            continue
        option_id = str(option.get("id") or "").strip()
        label = str(option.get("label") or "").strip()
        if not option_id or not label:
            continue
        tally = tallies.get(option_id, {}) if isinstance(tallies.get(option_id), dict) else {}
        options_payload.append(
            {
                "id": option_id,
                "label": label,
                "mn_spent": int(tally.get("mn_spent", 0) or 0),
                "vote_power": float(tally.get("vote_power", 0.0) or 0.0),
                "vote_count": int(tally.get("vote_count", 0) or 0),
            }
        )

    options_payload.sort(key=lambda option: float(option.get("vote_power", 0.0)), reverse=True)

    user_vote_summary = {
        "staked_maps_count": 0,
        "stake_weight_multiplier": 1.0,
        "mn_spent": 0,
        "vote_power": 0.0,
    }
    if user:
        normalized_staked_ids, _ = normalize_staked_map_ids(user, maps_collection)
        staked_maps_count = len(normalized_staked_ids)
        user_vote_summary["staked_maps_count"] = staked_maps_count
        user_vote_summary["stake_weight_multiplier"] = vote_weight_multiplier(staked_maps_count)

        aggregate = list(
            governance_votes.aggregate(
                [
                    {
                        "$match": {
                            "session_id": str(session_doc.get("session_id") or ""),
                            "username": str(user.get("username") or ""),
                        }
                    },
                    {
                        "$group": {
                            "_id": None,
                            "mn_spent": {"$sum": "$mn_spent"},
                            "vote_power": {"$sum": "$vote_power"},
                        }
                    },
                ]
            )
        )
        if aggregate:
            user_vote_summary["mn_spent"] = int(aggregate[0].get("mn_spent", 0) or 0)
            user_vote_summary["vote_power"] = float(aggregate[0].get("vote_power", 0.0) or 0.0)

    started_at = _parse_utc_datetime(session_doc.get("started_at"))
    ends_at = _parse_utc_datetime(session_doc.get("ends_at"))
    closed_at = _parse_utc_datetime(session_doc.get("closed_at"))

    return {
        "session_id": str(session_doc.get("session_id") or ""),
        "title": str(session_doc.get("title") or ""),
        "description": str(session_doc.get("description") or ""),
        "status": str(session_doc.get("status") or "closed"),
        "started_by": str(session_doc.get("started_by") or ""),
        "started_at": started_at.isoformat() if started_at else None,
        "ends_at": ends_at.isoformat() if ends_at else None,
        "closed_at": closed_at.isoformat() if closed_at else None,
        "total_mn_spent": int(session_doc.get("total_mn_spent", 0) or 0),
        "total_vote_power": float(session_doc.get("total_vote_power", 0.0) or 0.0),
        "unique_voter_count": len(list(session_doc.get("voters", []) or [])),
        "options": options_payload,
        "user_vote_summary": user_vote_summary,
    }


@router.get("/session")
@limiter.limit("30/minute")
def get_governance_session(request: Request, username: str):
    user = _sync_user(username)
    active_session = _get_active_session()
    latest_closed = governance_sessions.find_one(
        {"status": "closed"},
        sort=[("closed_at", -1), ("started_at", -1)],
    )

    return {
        "status": "success",
        "voting_open": active_session is not None,
        "is_bank_user": _is_bank_user(username),
        "active_session": _serialize_session(active_session, user=user),
        "latest_closed_session": _serialize_session(latest_closed, user=user),
        "user": serialize_session_user(user, maps_collection),
    }


@router.post("/session/start")
@limiter.limit("8/minute")
def start_governance_session(request: Request, payload: GovernanceStartPayload):
    username = str(payload.username or "").strip()
    if not _is_bank_user(username):
        raise HTTPException(status_code=403, detail="Only enigma_bank can start governance voting")

    _sync_user(username)
    if _get_active_session():
        raise HTTPException(status_code=409, detail="An active governance session already exists")

    options = _normalize_options(payload.options)
    now = _utc_now()
    session_id = f"gov-{uuid4().hex[:10]}"
    normalized_options = [{"id": f"opt-{index + 1}", "label": option} for index, option in enumerate(options)]
    tallies = {
        option["id"]: {
            "mn_spent": 0,
            "vote_power": 0.0,
            "vote_count": 0,
        }
        for option in normalized_options
    }

    governance_sessions.insert_one(
        {
            "session_id": session_id,
            "title": payload.title.strip(),
            "description": payload.description.strip(),
            "status": "active",
            "started_by": SYSTEM_BANK_USERNAME,
            "started_at": now,
            "ends_at": now + timedelta(hours=int(payload.duration_hours or 24)),
            "closed_at": None,
            "updated_at": now,
            "options": normalized_options,
            "tallies": tallies,
            "total_mn_spent": 0,
            "total_vote_power": 0.0,
            "voters": [],
        }
    )

    active_session = _get_active_session()
    bank_user = _sync_user(username)
    return {
        "status": "success",
        "voting_open": True,
        "is_bank_user": True,
        "active_session": _serialize_session(active_session, user=bank_user),
        "user": serialize_session_user(bank_user, maps_collection),
    }


@router.post("/session/close")
@limiter.limit("8/minute")
def close_governance_session(request: Request, payload: GovernanceClosePayload):
    username = str(payload.username or "").strip()
    if not _is_bank_user(username):
        raise HTTPException(status_code=403, detail="Only enigma_bank can close governance voting")

    _sync_user(username)
    active_session = _get_active_session()
    if not active_session:
        raise HTTPException(status_code=404, detail="No active governance session to close")

    now = _utc_now()
    governance_sessions.update_one(
        {"_id": active_session["_id"], "status": "active"},
        {"$set": {"status": "closed", "closed_at": now, "updated_at": now}},
    )

    closed_session = governance_sessions.find_one({"_id": active_session["_id"]}) or active_session
    bank_user = _sync_user(username)
    return {
        "status": "success",
        "voting_open": False,
        "is_bank_user": True,
        "closed_session": _serialize_session(closed_session, user=bank_user),
        "user": serialize_session_user(bank_user, maps_collection),
    }


@router.post("/vote")
@limiter.limit("60/minute")
def submit_governance_vote(request: Request, payload: GovernanceVotePayload):
    username = str(payload.username or "").strip()
    if _is_bank_user(username):
        raise HTTPException(status_code=403, detail="System accounts cannot vote")

    user = _sync_user(username)
    active_session = _get_active_session()
    if not active_session:
        raise HTTPException(status_code=409, detail="Voting is not open right now")

    option_id = str(payload.option_id or "").strip()
    options = [option for option in list(active_session.get("options", []) or []) if isinstance(option, dict)]
    option_lookup = {str(option.get("id") or "").strip(): option for option in options}
    if option_id not in option_lookup:
        raise HTTPException(status_code=400, detail="Invalid voting option")

    mn_spent = int(payload.mn_spent or 0)
    if mn_spent <= 0:
        raise HTTPException(status_code=400, detail="Vote amount must be greater than zero")

    normalized_staked_ids, _ = normalize_staked_map_ids(user, maps_collection)
    if normalized_staked_ids != list(user.get("staked_map_ids", []) or []):
        users_collection.update_one({"_id": user["_id"]}, {"$set": {"staked_map_ids": normalized_staked_ids}})
        user = _sync_user(username)
        normalized_staked_ids, _ = normalize_staked_map_ids(user, maps_collection)

    multiplier = vote_weight_multiplier(len(normalized_staked_ids))
    vote_power = round(mn_spent * multiplier, 4)
    now = _utc_now()

    governance_sessions.create_index("session_id", unique=True)
    governance_votes.create_index([("session_id", 1), ("username", 1), ("created_at", 1)])

    try:
        with client.start_session() as mongo_session:
            with mongo_session.start_transaction():
                debit_result = users_collection.update_one(
                    {"_id": user["_id"], "maze_nuggets": {"$gte": mn_spent}},
                    {"$inc": {"maze_nuggets": -mn_spent}},
                    session=mongo_session,
                )
                if debit_result.modified_count != 1:
                    raise HTTPException(status_code=409, detail="Not enough Maze Nuggets to cast that vote")

                credit_bank_dividend(users_collection, mn_spent, session=mongo_session)

                governance_update = governance_sessions.update_one(
                    {"_id": active_session["_id"], "status": "active"},
                    {
                        "$inc": {
                            "total_mn_spent": mn_spent,
                            "total_vote_power": vote_power,
                            f"tallies.{option_id}.mn_spent": mn_spent,
                            f"tallies.{option_id}.vote_power": vote_power,
                            f"tallies.{option_id}.vote_count": 1,
                        },
                        "$set": {"updated_at": now},
                        "$addToSet": {"voters": username},
                    },
                    session=mongo_session,
                )
                if governance_update.modified_count != 1:
                    raise HTTPException(status_code=409, detail="Voting session is no longer active")

                governance_votes.insert_one(
                    {
                        "session_id": str(active_session.get("session_id") or ""),
                        "username": username,
                        "option_id": option_id,
                        "mn_spent": mn_spent,
                        "stake_weight_multiplier": multiplier,
                        "vote_power": vote_power,
                        "staked_maps_count": len(normalized_staked_ids),
                        "created_at": now,
                    },
                    session=mongo_session,
                )
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail="Unable to record governance vote") from exc

    refreshed_user = _sync_user(username)
    refreshed_active = _get_active_session()
    return {
        "status": "success",
        "mn_spent": mn_spent,
        "vote_power": vote_power,
        "stake_weight_multiplier": multiplier,
        "voting_open": refreshed_active is not None,
        "active_session": _serialize_session(refreshed_active, user=refreshed_user),
        "user": serialize_session_user(refreshed_user, maps_collection),
    }
