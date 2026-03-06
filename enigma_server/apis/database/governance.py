from __future__ import annotations

import hashlib
import threading
import time
from datetime import datetime, timedelta, timezone
from typing import Any
from uuid import uuid4

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, Field

from main import limiter

from .db import client, governance_sessions, governance_votes, maps_collection, users_collection
from .economy_rules import credit_bank_dividend
from .staking_rules import normalize_staked_map_ids, vote_multiplier_breakdown
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    build_owned_maps_sync_update,
    build_user_defaults_update,
    serialize_session_user,
)

router = APIRouter(prefix="/database/governance")

VOTE_TYPE_ONE_CHOICE = "one_choice"
VOTE_TYPE_MULTIPLE_CHOICE = "multiple_choice"
VOTE_TYPE_TEXT_ENTRY = "text_entry"
VOTE_TYPE_NUMBER_SELECTION = "number_selection"
SUPPORTED_VOTE_TYPES = {
    VOTE_TYPE_ONE_CHOICE,
    VOTE_TYPE_MULTIPLE_CHOICE,
    VOTE_TYPE_TEXT_ENTRY,
    VOTE_TYPE_NUMBER_SELECTION,
}
SUPPORTED_DURATION_UNITS = {"hours", "days", "weeks"}
GOVERNANCE_SESSION_CACHE_TTL_SECONDS = 2.0
MAX_GOVERNANCE_SESSION_CACHE_ENTRIES = 4000
_governance_cache_lock = threading.Lock()
_governance_session_cache: dict[str, tuple[float, dict[str, Any]]] = {}


class GovernanceStartPayload(BaseModel):
    username: str
    title: str = Field(min_length=3, max_length=120)
    description: str = Field(default="", max_length=500)
    vote_type: str = Field(default=VOTE_TYPE_ONE_CHOICE)
    options: list[str] = Field(default_factory=list, min_length=0, max_length=12)
    duration_value: int = Field(default=24, ge=1, le=9999)
    duration_unit: str = Field(default="hours")
    vote_cost_mn: int = Field(default=10, ge=1, le=1_000_000)
    number_min: int | None = None
    number_max: int | None = None


class GovernanceClosePayload(BaseModel):
    username: str


class GovernanceVotePayload(BaseModel):
    username: str
    option_id: str | None = None
    option_ids: list[str] = Field(default_factory=list, max_length=16)
    text_entry: str | None = None
    number_entry: int | None = None
    vote_quantity: int = Field(default=1, ge=1, le=1000)


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


def _normalize_vote_type(value: Any) -> str:
    normalized = str(value or "").strip().lower()
    if normalized not in SUPPORTED_VOTE_TYPES:
        raise HTTPException(status_code=400, detail="Unsupported vote type")
    return normalized


def _normalize_duration_unit(value: Any) -> str:
    normalized = str(value or "").strip().lower()
    if normalized in {"hour", "hours"}:
        return "hours"
    if normalized in {"day", "days"}:
        return "days"
    if normalized in {"week", "weeks"}:
        return "weeks"
    raise HTTPException(status_code=400, detail="Unsupported duration unit")


def _duration_delta(value: int, unit: str) -> timedelta:
    if unit == "hours":
        return timedelta(hours=value)
    if unit == "days":
        return timedelta(days=value)
    if unit == "weeks":
        return timedelta(weeks=value)
    raise HTTPException(status_code=400, detail="Unsupported duration unit")


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


def _load_user_or_404(username: str) -> dict[str, Any]:
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")
    return user


def _prune_governance_session_cache_locked(now_monotonic: float) -> None:
    expired_keys = [
        cache_key
        for cache_key, (expires_at, _) in _governance_session_cache.items()
        if now_monotonic >= float(expires_at or 0.0)
    ]
    for cache_key in expired_keys:
        _governance_session_cache.pop(cache_key, None)

    if len(_governance_session_cache) <= MAX_GOVERNANCE_SESSION_CACHE_ENTRIES:
        return

    overflow = len(_governance_session_cache) - MAX_GOVERNANCE_SESSION_CACHE_ENTRIES
    oldest_keys = sorted(
        _governance_session_cache.items(),
        key=lambda entry: float(entry[1][0] or 0.0),
    )[:overflow]
    for cache_key, _ in oldest_keys:
        _governance_session_cache.pop(cache_key, None)


def _governance_session_cache_get(username: str) -> dict[str, Any] | None:
    cache_key = str(username or "").strip().lower()
    if not cache_key:
        return None

    now_monotonic = time.monotonic()
    cached = _governance_session_cache.get(cache_key)
    if cached and now_monotonic < float(cached[0] or 0.0):
        return cached[1]

    with _governance_cache_lock:
        now_monotonic = time.monotonic()
        _prune_governance_session_cache_locked(now_monotonic)
        cached = _governance_session_cache.get(cache_key)
        if cached and now_monotonic < float(cached[0] or 0.0):
            return cached[1]

    return None


def _governance_session_cache_set(username: str, payload: dict[str, Any]) -> None:
    cache_key = str(username or "").strip().lower()
    if not cache_key:
        return

    now_monotonic = time.monotonic()
    with _governance_cache_lock:
        _prune_governance_session_cache_locked(now_monotonic)
        _governance_session_cache[cache_key] = (
            now_monotonic + GOVERNANCE_SESSION_CACHE_TTL_SECONDS,
            payload,
        )


def _clear_governance_session_cache() -> None:
    with _governance_cache_lock:
        _governance_session_cache.clear()


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
        _clear_governance_session_cache()
        return None

    return active


def _serialize_session(
    session_doc: dict[str, Any] | None,
    user: dict[str, Any] | None = None,
) -> dict[str, Any] | None:
    if not session_doc:
        return None

    options_from_session = {}
    for option in list(session_doc.get("options", []) or []):
        if not isinstance(option, dict):
            continue
        option_id = str(option.get("id") or "").strip()
        option_label = str(option.get("label") or "").strip()
        if option_id and option_label:
            options_from_session[option_id] = option_label

    tallies = session_doc.get("tallies", {}) if isinstance(session_doc.get("tallies"), dict) else {}
    payload_entries: list[dict[str, Any]] = []

    def append_entry(entry_id: str, fallback_label: str) -> None:
        tally = tallies.get(entry_id, {}) if isinstance(tallies.get(entry_id), dict) else {}
        payload_entries.append(
            {
                "id": entry_id,
                "label": str(tally.get("label") or fallback_label),
                "mn_spent": int(tally.get("mn_spent", 0) or 0),
                "vote_power": float(tally.get("vote_power", 0.0) or 0.0),
                "vote_count": int(tally.get("vote_count", 0) or 0),
                "vote_quantity": int(tally.get("vote_quantity", 0) or 0),
            }
        )

    for option_id, option_label in options_from_session.items():
        append_entry(option_id, option_label)

    for tally_key, tally_value in tallies.items():
        if tally_key in options_from_session or not isinstance(tally_value, dict):
            continue
        append_entry(str(tally_key), str(tally_value.get("label") or tally_key))

    payload_entries.sort(
        key=lambda entry: (
            float(entry.get("vote_power", 0.0) or 0.0),
            int(entry.get("mn_spent", 0) or 0),
            int(entry.get("vote_quantity", 0) or 0),
            str(entry.get("label") or "").lower(),
        ),
        reverse=True,
    )

    user_vote_summary = {
        "staked_maps_count": 0,
        "stake_weight_multiplier": 1.0,
        "effective_vote_multiplier": 1.0,
        "stake_multiplier": 1.0,
        "participation_multiplier": 1.0,
        "raw_multiplier": 1.0,
        "multiplier_cap": 2.25,
        "participation_votes_count": 0,
        "mn_spent": 0,
        "vote_power": 0.0,
        "vote_quantity": 0,
    }
    if user:
        normalized_staked_ids, _ = normalize_staked_map_ids(user, maps_collection)
        staked_maps_count = len(normalized_staked_ids)
        participation_votes_count = governance_votes.count_documents(
            {"username": str(user.get("username") or "")}
        )
        multiplier_breakdown = vote_multiplier_breakdown(staked_maps_count, participation_votes_count)
        user_vote_summary["staked_maps_count"] = staked_maps_count
        user_vote_summary["stake_weight_multiplier"] = float(multiplier_breakdown["effective_multiplier"])
        user_vote_summary["effective_vote_multiplier"] = float(multiplier_breakdown["effective_multiplier"])
        user_vote_summary["stake_multiplier"] = float(multiplier_breakdown["stake_multiplier"])
        user_vote_summary["participation_multiplier"] = float(multiplier_breakdown["participation_multiplier"])
        user_vote_summary["raw_multiplier"] = float(multiplier_breakdown["raw_multiplier"])
        user_vote_summary["multiplier_cap"] = float(multiplier_breakdown["multiplier_cap"])
        user_vote_summary["participation_votes_count"] = int(multiplier_breakdown["participation_votes_count"])

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
                            "vote_quantity": {"$sum": "$vote_quantity"},
                        }
                    },
                ]
            )
        )
        if aggregate:
            user_vote_summary["mn_spent"] = int(aggregate[0].get("mn_spent", 0) or 0)
            user_vote_summary["vote_power"] = float(aggregate[0].get("vote_power", 0.0) or 0.0)
            user_vote_summary["vote_quantity"] = int(aggregate[0].get("vote_quantity", 0) or 0)

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
        "vote_type": str(session_doc.get("vote_type") or VOTE_TYPE_ONE_CHOICE),
        "vote_cost_mn": int(session_doc.get("vote_cost_mn", 1) or 1),
        "duration_value": int(session_doc.get("duration_value", 24) or 24),
        "duration_unit": str(session_doc.get("duration_unit") or "hours"),
        "number_min": session_doc.get("number_min"),
        "number_max": session_doc.get("number_max"),
        "total_mn_spent": int(session_doc.get("total_mn_spent", 0) or 0),
        "total_vote_power": float(session_doc.get("total_vote_power", 0.0) or 0.0),
        "total_votes_cast": int(session_doc.get("total_votes_cast", 0) or 0),
        "unique_voter_count": len(list(session_doc.get("voters", []) or [])),
        "options": payload_entries,
        "user_vote_summary": user_vote_summary,
    }


def _session_result_leader_label(session_doc: dict[str, Any] | None) -> str | None:
    if not isinstance(session_doc, dict):
        return None

    tallies = session_doc.get("tallies")
    if not isinstance(tallies, dict) or not tallies:
        return None

    best_label: str | None = None
    best_power = float("-inf")
    best_spent = int(-1)
    for entry in tallies.values():
        if not isinstance(entry, dict):
            continue
        label = str(entry.get("label") or "").strip()
        if not label:
            continue
        vote_power = float(entry.get("vote_power", 0.0) or 0.0)
        mn_spent = int(entry.get("mn_spent", 0) or 0)
        if vote_power > best_power or (vote_power == best_power and mn_spent > best_spent):
            best_label = label
            best_power = vote_power
            best_spent = mn_spent

    return best_label


def _serialize_recent_votes(username: str, limit: int = 20) -> list[dict[str, Any]]:
    normalized_username = str(username or "").strip()
    if not normalized_username:
        return []

    vote_docs = list(
        governance_votes.find(
            {"username": normalized_username},
            {
                "session_id": 1,
                "vote_type": 1,
                "selection_labels": 1,
                "vote_quantity": 1,
                "mn_spent": 1,
                "vote_power": 1,
                "stake_weight_multiplier": 1,
                "created_at": 1,
            },
        )
        .sort("created_at", -1)
        .limit(max(1, min(100, int(limit or 20))))
    )
    if not vote_docs:
        return []

    session_ids = sorted(
        {
            str(doc.get("session_id") or "").strip()
            for doc in vote_docs
            if str(doc.get("session_id") or "").strip()
        }
    )
    session_lookup: dict[str, dict[str, Any]] = {}
    if session_ids:
        session_docs = governance_sessions.find(
            {"session_id": {"$in": session_ids}},
            {
                "session_id": 1,
                "title": 1,
                "status": 1,
                "tallies": 1,
            },
        )
        session_lookup = {
            str(doc.get("session_id") or "").strip(): doc
            for doc in session_docs
            if str(doc.get("session_id") or "").strip()
        }

    serialized: list[dict[str, Any]] = []
    for vote_doc in vote_docs:
        session_id = str(vote_doc.get("session_id") or "").strip()
        related_session = session_lookup.get(session_id, {})
        created_at = _parse_utc_datetime(vote_doc.get("created_at"))
        selections = [
            str(label).strip()
            for label in list(vote_doc.get("selection_labels", []) or [])
            if str(label).strip()
        ]
        serialized.append(
            {
                "session_id": session_id,
                "session_title": str(related_session.get("title") or "Governance Session"),
                "session_status": str(related_session.get("status") or "unknown"),
                "session_result_leader": _session_result_leader_label(related_session),
                "vote_type": str(vote_doc.get("vote_type") or ""),
                "selection_labels": selections,
                "vote_quantity": int(vote_doc.get("vote_quantity", 0) or 0),
                "mn_spent": int(vote_doc.get("mn_spent", 0) or 0),
                "vote_power": float(vote_doc.get("vote_power", 0.0) or 0.0),
                "stake_weight_multiplier": float(vote_doc.get("stake_weight_multiplier", 1.0) or 1.0),
                "created_at": created_at.isoformat() if created_at else None,
            }
        )

    return serialized


def _normalize_unique_ids(raw_ids: list[str]) -> list[str]:
    dedupe: set[str] = set()
    normalized: list[str] = []
    for raw_id in raw_ids:
        option_id = str(raw_id or "").strip()
        if not option_id or option_id in dedupe:
            continue
        dedupe.add(option_id)
        normalized.append(option_id)
    return normalized


def _normalize_text_vote_value(value: Any) -> str:
    return str(value or "").strip()[:120]


def _text_tally_key(value: str) -> str:
    normalized = _normalize_text_vote_value(value).casefold()
    digest = hashlib.sha1(normalized.encode("utf-8")).hexdigest()[:24]
    return f"text:{digest}"


@router.get("/session")
@limiter.limit("30/minute")
def get_governance_session(request: Request, username: str):
    normalized_username = str(username or "").strip()
    cached_payload = _governance_session_cache_get(normalized_username)
    if cached_payload is not None:
        return cached_payload

    user = _load_user_or_404(normalized_username)
    active_session = _get_active_session()
    latest_closed = governance_sessions.find_one(
        {"status": "closed"},
        sort=[("closed_at", -1), ("started_at", -1)],
    )
    normalized_user_username = str(user.get("username") or "")
    recent_votes = _serialize_recent_votes(normalized_user_username)

    payload = {
        "status": "success",
        "voting_open": active_session is not None,
        "is_bank_user": _is_bank_user(normalized_username),
        "active_session": _serialize_session(active_session, user=user),
        "latest_closed_session": _serialize_session(latest_closed, user=user),
        "recent_votes": recent_votes,
        "user": serialize_session_user(user, maps_collection, include_maps=False),
    }
    _governance_session_cache_set(normalized_user_username, payload)
    return payload


@router.post("/session/start")
@limiter.limit("8/minute")
def start_governance_session(request: Request, payload: GovernanceStartPayload):
    username = str(payload.username or "").strip()
    if not _is_bank_user(username):
        raise HTTPException(status_code=403, detail="Only enigma_bank can start governance voting")

    _sync_user(username)
    if _get_active_session():
        raise HTTPException(status_code=409, detail="An active governance session already exists")

    vote_type = _normalize_vote_type(payload.vote_type)
    duration_unit = _normalize_duration_unit(payload.duration_unit)
    duration_value = int(payload.duration_value or 24)
    vote_cost_mn = int(payload.vote_cost_mn or 1)
    if vote_cost_mn <= 0:
        raise HTTPException(status_code=400, detail="Vote cost must be greater than zero")

    options: list[str] = []
    if vote_type in {VOTE_TYPE_ONE_CHOICE, VOTE_TYPE_MULTIPLE_CHOICE}:
        options = _normalize_options(payload.options)

    number_min = payload.number_min
    number_max = payload.number_max
    if vote_type == VOTE_TYPE_NUMBER_SELECTION:
        if number_min is None or number_max is None:
            raise HTTPException(status_code=400, detail="Number selection votes require min and max values")
        if int(number_min) > int(number_max):
            raise HTTPException(status_code=400, detail="Number selection min cannot be greater than max")
    else:
        number_min = None
        number_max = None

    now = _utc_now()
    session_id = f"gov-{uuid4().hex[:10]}"
    normalized_options = [{"id": f"opt-{index + 1}", "label": option} for index, option in enumerate(options)]
    tallies = {
        option["id"]: {
            "label": option["label"],
            "mn_spent": 0,
            "vote_power": 0.0,
            "vote_count": 0,
            "vote_quantity": 0,
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
            "ends_at": now + _duration_delta(duration_value, duration_unit),
            "closed_at": None,
            "updated_at": now,
            "vote_type": vote_type,
            "vote_cost_mn": vote_cost_mn,
            "duration_value": duration_value,
            "duration_unit": duration_unit,
            "number_min": number_min,
            "number_max": number_max,
            "options": normalized_options,
            "tallies": tallies,
            "total_mn_spent": 0,
            "total_vote_power": 0.0,
            "total_votes_cast": 0,
            "voters": [],
        }
    )

    _clear_governance_session_cache()
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
    _clear_governance_session_cache()

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
@limiter.limit("90/minute")
def submit_governance_vote(request: Request, payload: GovernanceVotePayload):
    username = str(payload.username or "").strip()
    if _is_bank_user(username):
        raise HTTPException(status_code=403, detail="System accounts cannot vote")

    user = _sync_user(username)
    active_session = _get_active_session()
    if not active_session:
        raise HTTPException(status_code=409, detail="Voting is not open right now")

    vote_type = _normalize_vote_type(active_session.get("vote_type"))
    option_lookup = {
        str(option.get("id") or "").strip(): str(option.get("label") or "").strip()
        for option in list(active_session.get("options", []) or [])
        if isinstance(option, dict)
    }

    raw_ids = list(payload.option_ids or [])
    if payload.option_id:
        raw_ids.append(payload.option_id)
    selected_ids = _normalize_unique_ids(raw_ids)

    selections: list[tuple[str, str]] = []
    if vote_type == VOTE_TYPE_ONE_CHOICE:
        if not selected_ids:
            raise HTTPException(status_code=400, detail="Choose one option")
        selected_id = selected_ids[0]
        if selected_id not in option_lookup:
            raise HTTPException(status_code=400, detail="Invalid voting option")
        selections = [(selected_id, option_lookup[selected_id])]
    elif vote_type == VOTE_TYPE_MULTIPLE_CHOICE:
        if not selected_ids:
            raise HTTPException(status_code=400, detail="Choose at least one option")
        for selected_id in selected_ids:
            if selected_id not in option_lookup:
                raise HTTPException(status_code=400, detail="Invalid voting option")
            selections.append((selected_id, option_lookup[selected_id]))
    elif vote_type == VOTE_TYPE_TEXT_ENTRY:
        tallies = active_session.get("tallies")
        text_option_lookup: dict[str, str] = {}
        text_lookup_by_label: dict[str, tuple[str, str]] = {}
        if isinstance(tallies, dict):
            for tally_key, tally_value in tallies.items():
                option_id = str(tally_key or "").strip()
                if not option_id.startswith("text:") or not isinstance(tally_value, dict):
                    continue
                option_label = str(tally_value.get("label") or "").strip()
                if option_label:
                    text_option_lookup[option_id] = option_label
                    text_lookup_by_label.setdefault(option_label.casefold(), (option_id, option_label))

        text_value = str(payload.text_entry or "").strip()
        has_new_entry = bool(text_value)

        if len(selected_ids) > 1:
            raise HTTPException(status_code=400, detail="Choose one text entry to upvote")
        if selected_ids and has_new_entry:
            raise HTTPException(
                status_code=400,
                detail="Choose either an existing text entry to upvote or submit a new entry",
            )

        if selected_ids:
            selected_id = selected_ids[0]
            selected_label = text_option_lookup.get(selected_id)
            if not selected_label:
                raise HTTPException(status_code=400, detail="Invalid text entry selection")
            selections = [(selected_id, selected_label)]
        elif has_new_entry:
            text_value = _normalize_text_vote_value(text_value)
            existing_match = text_lookup_by_label.get(text_value.casefold())
            if existing_match:
                selections = [existing_match]
            else:
                text_key = _text_tally_key(text_value)
                selections = [(text_key, text_value)]
        else:
            raise HTTPException(status_code=400, detail="Choose a text entry to upvote or submit a new one")
    else:
        if payload.number_entry is None:
            raise HTTPException(status_code=400, detail="Number entry is required for this voting type")
        number_value = int(payload.number_entry)
        min_value = active_session.get("number_min")
        max_value = active_session.get("number_max")
        if min_value is not None and number_value < int(min_value):
            raise HTTPException(status_code=400, detail="Submitted number is below the allowed minimum")
        if max_value is not None and number_value > int(max_value):
            raise HTTPException(status_code=400, detail="Submitted number is above the allowed maximum")
        selections = [(f"number:{number_value}", str(number_value))]

    vote_quantity = int(payload.vote_quantity or 1)
    if vote_quantity <= 0:
        raise HTTPException(status_code=400, detail="Vote quantity must be greater than zero")

    unit_vote_cost = max(1, int(active_session.get("vote_cost_mn", 1) or 1))
    selection_count = len(selections)
    total_vote_units = vote_quantity * selection_count
    mn_spent = unit_vote_cost * total_vote_units

    normalized_staked_ids, _ = normalize_staked_map_ids(user, maps_collection)
    if normalized_staked_ids != list(user.get("staked_map_ids", []) or []):
        users_collection.update_one({"_id": user["_id"]}, {"$set": {"staked_map_ids": normalized_staked_ids}})
        user = _sync_user(username)
        normalized_staked_ids, _ = normalize_staked_map_ids(user, maps_collection)

    prior_votes_count = governance_votes.count_documents({"username": username})
    multiplier_breakdown = vote_multiplier_breakdown(len(normalized_staked_ids), prior_votes_count)
    multiplier = float(multiplier_breakdown["effective_multiplier"])
    stake_multiplier = float(multiplier_breakdown["stake_multiplier"])
    participation_multiplier = float(multiplier_breakdown["participation_multiplier"])
    raw_multiplier = float(multiplier_breakdown["raw_multiplier"])
    multiplier_cap = float(multiplier_breakdown["multiplier_cap"])
    vote_power = round(mn_spent * multiplier, 4)
    per_selection_spent = unit_vote_cost * vote_quantity
    per_selection_power = round(per_selection_spent * multiplier, 4)
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

                credit_bank_dividend(users_collection, mn_spent, mongo_session=mongo_session)

                inc_payload: dict[str, int | float] = {
                    "total_mn_spent": mn_spent,
                    "total_vote_power": vote_power,
                    "total_votes_cast": total_vote_units,
                }
                set_payload: dict[str, Any] = {
                    "updated_at": now,
                }
                for entry_key, entry_label in selections:
                    inc_payload[f"tallies.{entry_key}.mn_spent"] = per_selection_spent
                    inc_payload[f"tallies.{entry_key}.vote_power"] = per_selection_power
                    inc_payload[f"tallies.{entry_key}.vote_count"] = 1
                    inc_payload[f"tallies.{entry_key}.vote_quantity"] = vote_quantity
                    set_payload[f"tallies.{entry_key}.label"] = entry_label

                governance_update = governance_sessions.update_one(
                    {"_id": active_session["_id"], "status": "active"},
                    {
                        "$inc": inc_payload,
                        "$set": set_payload,
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
                        "vote_type": vote_type,
                        "selection_keys": [entry_key for entry_key, _ in selections],
                        "selection_labels": [entry_label for _, entry_label in selections],
                        "text_entry": str(payload.text_entry or "").strip()[:120] if vote_type == VOTE_TYPE_TEXT_ENTRY else None,
                        "number_entry": int(payload.number_entry) if vote_type == VOTE_TYPE_NUMBER_SELECTION and payload.number_entry is not None else None,
                        "vote_quantity": vote_quantity,
                        "selection_count": selection_count,
                        "vote_cost_mn": unit_vote_cost,
                        "mn_spent": mn_spent,
                        "stake_weight_multiplier": multiplier,
                        "effective_vote_multiplier": multiplier,
                        "stake_multiplier": stake_multiplier,
                        "participation_multiplier": participation_multiplier,
                        "raw_multiplier_before_cap": raw_multiplier,
                        "multiplier_cap": multiplier_cap,
                        "prior_votes_count": prior_votes_count,
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

    _clear_governance_session_cache()
    refreshed_user = _sync_user(username)
    refreshed_active = _get_active_session()
    latest_closed = governance_sessions.find_one(
        {"status": "closed"},
        sort=[("closed_at", -1), ("started_at", -1)],
    )
    return {
        "status": "success",
        "mn_spent": mn_spent,
        "vote_power": vote_power,
        "stake_weight_multiplier": multiplier,
        "effective_vote_multiplier": multiplier,
        "stake_multiplier": stake_multiplier,
        "participation_multiplier": participation_multiplier,
        "raw_multiplier_before_cap": raw_multiplier,
        "multiplier_cap": multiplier_cap,
        "prior_votes_count": prior_votes_count,
        "vote_quantity": vote_quantity,
        "vote_cost_mn": unit_vote_cost,
        "selection_count": selection_count,
        "voting_open": refreshed_active is not None,
        "active_session": _serialize_session(refreshed_active, user=refreshed_user),
        "latest_closed_session": _serialize_session(latest_closed, user=refreshed_user),
        "recent_votes": _serialize_recent_votes(username),
        "user": serialize_session_user(refreshed_user, maps_collection),
    }
