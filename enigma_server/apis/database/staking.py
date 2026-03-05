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
    stake_lock_expires_at,
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
    missing_lock_ids = [map_id for map_id in normalized_staked_ids if map_id not in normalized_lock_lookup]
    if missing_lock_ids:
        for map_id in missing_lock_ids:
            normalized_lock_lookup[map_id] = stake_lock_expires_at().isoformat()
    current_staked_ids = [str(value or "").strip() for value in list(user.get("staked_map_ids", []) or []) if str(value or "").strip()]
    current_lock_lookup = user.get("staked_map_lock_until") if isinstance(user.get("staked_map_lock_until"), dict) else {}
    if normalized_staked_ids != current_staked_ids or normalized_lock_lookup != current_lock_lookup:
        users_collection.update_one(
            {"_id": user["_id"]},
            {"$set": {"staked_map_ids": normalized_staked_ids, "staked_map_lock_until": normalized_lock_lookup}},
        )
        user = users_collection.find_one({"_id": user["_id"]}) or user
        overview = build_staking_overview(user, maps_collection)

    return {
        "status": "success",
        "overview": overview,
        "user": serialize_session_user(user, maps_collection),
    }


@router.post("/stake")
@limiter.limit("20/minute")
def stake_map(request: Request, payload: StakeMapPayload):
    username = str(payload.username or "").strip()
    user = _sync_user(username)
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    target_map_id = _resolve_owned_map_id(payload.map_id, payload.map_name, owned_lookup)

    should_persist = False
    if target_map_id not in normalized_staked_ids:
        normalized_staked_ids.append(target_map_id)
        normalized_lock_lookup[target_map_id] = stake_lock_expires_at().isoformat()
        should_persist = True
    elif target_map_id not in normalized_lock_lookup:
        # Legacy safety: if a staked map has no lock metadata, immediately apply a full lock window.
        normalized_lock_lookup[target_map_id] = stake_lock_expires_at().isoformat()
        should_persist = True

    if should_persist:
        users_collection.update_one(
            {"_id": user["_id"]},
            {"$set": {"staked_map_ids": normalized_staked_ids, "staked_map_lock_until": normalized_lock_lookup}},
        )

    refreshed = _sync_user(username)
    overview = build_staking_overview(refreshed, maps_collection)
    return {
        "status": "success",
        "overview": overview,
        "user": serialize_session_user(refreshed, maps_collection),
    }


@router.post("/unstake")
@limiter.limit("20/minute")
def unstake_map(request: Request, payload: UnstakeMapPayload):
    username = str(payload.username or "").strip()
    user = _sync_user(username)
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    target_map_id = _resolve_owned_map_id(payload.map_id, payload.map_name, owned_lookup)
    if target_map_id not in normalized_staked_ids:
        raise HTTPException(status_code=409, detail="This map is not currently staked")

    locked_until_iso = normalized_lock_lookup.get(target_map_id)
    if not locked_until_iso:
        enforced_lock_until = stake_lock_expires_at()
        normalized_lock_lookup[target_map_id] = enforced_lock_until.isoformat()
        users_collection.update_one(
            {"_id": user["_id"]},
            {"$set": {"staked_map_ids": normalized_staked_ids, "staked_map_lock_until": normalized_lock_lookup}},
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

    if updated_staked_ids != normalized_staked_ids:
        users_collection.update_one(
            {"_id": user["_id"]},
            {"$set": {"staked_map_ids": updated_staked_ids, "staked_map_lock_until": normalized_lock_lookup}},
        )

    refreshed = _sync_user(username)
    overview = build_staking_overview(refreshed, maps_collection)
    return {
        "status": "success",
        "overview": overview,
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
        "overview": overview,
        "user": serialize_session_user(refreshed, maps_collection),
    }
