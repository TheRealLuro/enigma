from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel

from main import limiter

from .db import maps_collection, users_collection
from .staking_rules import (
    build_staking_overview,
    claim_daily_staking_reward,
    normalize_staked_map_ids,
    normalize_staked_map_lock_until,
    normalize_staked_map_started_at,
    stake_lock_expires_at,
    vote_multiplier_breakdown,
)
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    build_owned_maps_sync_update,
    build_user_defaults_update,
    serialize_session_user,
)

router = APIRouter(prefix="/database/staking")


class StakeMapPayload(BaseModel):
    username: str
    map_id: str | None = None
    map_name: str | None = None


class UnstakeMapPayload(BaseModel):
    username: str
    map_id: str | None = None
    map_name: str | None = None


class ClaimStakingPayload(BaseModel):
    username: str


def _collective_research_snapshot() -> tuple[dict[str, Any], dict[str, Any]]:
    contributor_count = 0
    contributed_maps_total = 0

    for user_doc in users_collection.find({}, {"staked_map_ids": 1}):
        staked_ids = {
            str(value or "").strip()
            for value in list(user_doc.get("staked_map_ids", []) or [])
            if str(value or "").strip()
        }
        if not staked_ids:
            continue

        contributor_count += 1
        contributed_maps_total += len(staked_ids)

    target_maps = 500
    progress_percent = round(min(100.0, (contributed_maps_total / target_maps) * 100.0), 2)
    stability_percent = round(min(100.0, 35.0 + (progress_percent * 0.65)), 2)
    stability_status = (
        "Stable"
        if stability_percent >= 80
        else "Recovering"
        if stability_percent >= 55
        else "Volatile"
    )

    collective = {
        "title": "Decode Labyrinth Type IV",
        "description": "Global anomaly mapping objective coordinated by Enigma Research Labs. Current evidence suggests some structures may be intentionally constructed.",
        "target_maps": target_maps,
        "contributed_maps": contributed_maps_total,
        "contributor_count": contributor_count,
        "progress_percent": progress_percent,
    }
    stability = {
        "percent": stability_percent,
        "status": stability_status,
        "required_maps": target_maps,
    }
    return collective, stability


def _augment_research_metrics(overview: dict[str, Any]) -> dict[str, Any]:
    staked_maps_count = int(overview.get("staked_maps_count", 0) or 0)
    multiplier = vote_multiplier_breakdown(staked_maps_count, 0)
    collective, stability = _collective_research_snapshot()
    overview["research_influence"] = {
        "staked_maps_count": staked_maps_count,
        "multiplier": float(multiplier.get("effective_multiplier", 1.0) or 1.0),
        "stake_component": float(multiplier.get("stake_multiplier", 1.0) or 1.0),
        "description": "Research influence feeds into Council vote weight.",
    }
    overview["collective_research"] = collective
    overview["anomaly_stability"] = stability
    return overview


def _sync_user(username: str) -> dict[str, Any]:
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    if bool(user.get("is_system_account")) or username.strip().lower() == SYSTEM_BANK_USERNAME:
        raise HTTPException(status_code=403, detail="System accounts cannot use staking")

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


def _resolve_owned_map_id(
    map_id: str | None,
    map_name: str | None,
    owned_lookup: dict[str, dict[str, Any]],
) -> str:
    normalized_map_id = str(map_id or "").strip()
    if normalized_map_id:
        if normalized_map_id not in owned_lookup:
            raise HTTPException(status_code=404, detail="Only owned maps can be staked")
        return normalized_map_id

    normalized_map_name = str(map_name or "").strip().lower()
    if normalized_map_name:
        for owned_map_id, owned_map in owned_lookup.items():
            if str(owned_map.get("map_name") or "").strip().lower() == normalized_map_name:
                return owned_map_id
        raise HTTPException(status_code=404, detail="Owned map not found")

    raise HTTPException(status_code=400, detail="Map id or map name is required")


@router.get("/overview")
@limiter.limit("30/minute")
def get_staking_overview(request: Request, username: str):
    user = _sync_user(username)
    overview = build_staking_overview(user, maps_collection)
    normalized_staked_ids, _ = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    normalized_started_lookup = normalize_staked_map_started_at(user, normalized_staked_ids, normalized_lock_lookup)
    missing_lock_ids = [map_id for map_id in normalized_staked_ids if map_id not in normalized_lock_lookup]
    if missing_lock_ids:
        for map_id in missing_lock_ids:
            normalized_lock_lookup[map_id] = stake_lock_expires_at().isoformat()
    missing_started_ids = [map_id for map_id in normalized_staked_ids if map_id not in normalized_started_lookup]
    if missing_started_ids:
        normalized_started_lookup = normalize_staked_map_started_at(user, normalized_staked_ids, normalized_lock_lookup)
    current_staked_ids = [str(value or "").strip() for value in list(user.get("staked_map_ids", []) or []) if str(value or "").strip()]
    current_lock_lookup = user.get("staked_map_lock_until") if isinstance(user.get("staked_map_lock_until"), dict) else {}
    current_started_lookup = user.get("staked_map_started_at") if isinstance(user.get("staked_map_started_at"), dict) else {}
    if (
        normalized_staked_ids != current_staked_ids
        or normalized_lock_lookup != current_lock_lookup
        or normalized_started_lookup != current_started_lookup
    ):
        users_collection.update_one(
            {"_id": user["_id"]},
            {
                "$set": {
                    "staked_map_ids": normalized_staked_ids,
                    "staked_map_lock_until": normalized_lock_lookup,
                    "staked_map_started_at": normalized_started_lookup,
                }
            },
        )
        user = users_collection.find_one({"_id": user["_id"]}) or user
        overview = build_staking_overview(user, maps_collection)

    return {
        "status": "success",
        "overview": _augment_research_metrics(overview),
        "user": serialize_session_user(user, maps_collection),
    }


@router.post("/stake")
@limiter.limit("20/minute")
def stake_map(request: Request, payload: StakeMapPayload):
    username = str(payload.username or "").strip()
    user = _sync_user(username)
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    normalized_started_lookup = normalize_staked_map_started_at(user, normalized_staked_ids, normalized_lock_lookup)
    target_map_id = _resolve_owned_map_id(payload.map_id, payload.map_name, owned_lookup)

    should_persist = False
    now = datetime.now(timezone.utc)
    if target_map_id not in normalized_staked_ids:
        normalized_staked_ids.append(target_map_id)
        normalized_lock_lookup[target_map_id] = stake_lock_expires_at(now).isoformat()
        normalized_started_lookup[target_map_id] = now.isoformat()
        should_persist = True
    elif target_map_id not in normalized_lock_lookup:
        # Legacy safety: if a staked map has no lock metadata, immediately apply a full lock window.
        normalized_lock_lookup[target_map_id] = stake_lock_expires_at(now).isoformat()
        normalized_started_lookup[target_map_id] = now.isoformat()
        should_persist = True
    elif target_map_id not in normalized_started_lookup:
        normalized_started_lookup[target_map_id] = now.isoformat()
        should_persist = True

    if should_persist:
        users_collection.update_one(
            {"_id": user["_id"]},
            {
                "$set": {
                    "staked_map_ids": normalized_staked_ids,
                    "staked_map_lock_until": normalized_lock_lookup,
                    "staked_map_started_at": normalized_started_lookup,
                }
            },
        )

    refreshed = _sync_user(username)
    overview = build_staking_overview(refreshed, maps_collection)
    return {
        "status": "success",
        "overview": _augment_research_metrics(overview),
        "user": serialize_session_user(refreshed, maps_collection),
    }


@router.post("/unstake")
@limiter.limit("20/minute")
def unstake_map(request: Request, payload: UnstakeMapPayload):
    username = str(payload.username or "").strip()
    user = _sync_user(username)
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    normalized_started_lookup = normalize_staked_map_started_at(user, normalized_staked_ids, normalized_lock_lookup)
    target_map_id = _resolve_owned_map_id(payload.map_id, payload.map_name, owned_lookup)
    if target_map_id not in normalized_staked_ids:
        raise HTTPException(status_code=409, detail="This map is not currently staked")

    locked_until_iso = normalized_lock_lookup.get(target_map_id)
    if not locked_until_iso:
        enforced_lock_until = stake_lock_expires_at()
        normalized_lock_lookup[target_map_id] = enforced_lock_until.isoformat()
        normalized_started_lookup[target_map_id] = datetime.now(timezone.utc).isoformat()
        users_collection.update_one(
            {"_id": user["_id"]},
            {
                "$set": {
                    "staked_map_ids": normalized_staked_ids,
                    "staked_map_lock_until": normalized_lock_lookup,
                    "staked_map_started_at": normalized_started_lookup,
                }
            },
        )
        raise HTTPException(
            status_code=409,
            detail=f"This map is locked in staking until {enforced_lock_until.isoformat()}",
        )

    if locked_until_iso:
        lock_expires_at = datetime.fromisoformat(locked_until_iso.replace("Z", "+00:00"))
        if lock_expires_at.tzinfo is None:
            lock_expires_at = lock_expires_at.replace(tzinfo=timezone.utc)
        if lock_expires_at.astimezone(timezone.utc) > datetime.now(timezone.utc):
            raise HTTPException(
                status_code=409,
                detail=f"This map is locked in staking until {lock_expires_at.astimezone(timezone.utc).isoformat()}",
            )
    updated_staked_ids = [map_id for map_id in normalized_staked_ids if map_id != target_map_id]
    if target_map_id in normalized_lock_lookup:
        del normalized_lock_lookup[target_map_id]
    if target_map_id in normalized_started_lookup:
        del normalized_started_lookup[target_map_id]

    if updated_staked_ids != normalized_staked_ids:
        users_collection.update_one(
            {"_id": user["_id"]},
            {
                "$set": {
                    "staked_map_ids": updated_staked_ids,
                    "staked_map_lock_until": normalized_lock_lookup,
                    "staked_map_started_at": normalized_started_lookup,
                }
            },
        )

    refreshed = _sync_user(username)
    overview = build_staking_overview(refreshed, maps_collection)
    return {
        "status": "success",
        "overview": _augment_research_metrics(overview),
        "user": serialize_session_user(refreshed, maps_collection),
    }


@router.post("/claim")
@limiter.limit("10/minute")
def claim_staking_reward(request: Request, payload: ClaimStakingPayload):
    username = str(payload.username or "").strip()
    user = _sync_user(username)
    claim_result = claim_daily_staking_reward(users_collection, maps_collection, user)
    refreshed = _sync_user(username)
    overview = build_staking_overview(refreshed, maps_collection)

    return {
        "status": "success",
        "reward_granted": bool(claim_result.get("reward_granted")),
        "rewarded_mn": int(claim_result.get("rewarded_mn", 0) or 0),
        "overview": _augment_research_metrics(overview),
        "user": serialize_session_user(refreshed, maps_collection),
    }
