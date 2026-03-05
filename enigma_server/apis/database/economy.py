from __future__ import annotations

from datetime import datetime, timedelta, timezone
from typing import Any

from fastapi import APIRouter, HTTPException, Request

from main import limiter

from .db import governance_sessions, governance_votes, maps_collection, marketplace_collection, users_collection
from .staking_rules import normalize_staked_map_ids
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    apply_user_defaults,
    build_owned_maps_sync_update,
    build_user_defaults_update,
    is_user_online,
    serialize_session_user,
)

router = APIRouter(prefix="/database/economy")


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


def _is_system_account(user: dict[str, Any]) -> bool:
    username = str(user.get("username") or "").strip().lower()
    return bool(user.get("is_system_account")) or username == SYSTEM_BANK_USERNAME


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


def _active_within_window(user: dict[str, Any], now: datetime, window: timedelta) -> bool:
    last_seen_at = _parse_utc_datetime(user.get("last_seen_at"))
    last_login_at = _parse_utc_datetime(user.get("last_login_at"))
    latest = max(
        [value for value in [last_seen_at, last_login_at] if value is not None],
        default=None,
    )
    if latest is None:
        return False
    return (now - latest) <= window


def _get_listing_value_stats() -> tuple[int, float]:
    aggregate = list(
        marketplace_collection.aggregate(
            [
                {
                    "$group": {
                        "_id": None,
                        "total_value": {"$sum": "$price"},
                        "avg_value": {"$avg": "$price"},
                    }
                }
            ]
        )
    )
    if not aggregate:
        return 0, 0.0
    return int(aggregate[0].get("total_value", 0) or 0), float(aggregate[0].get("avg_value", 0.0) or 0.0)


def _get_total_governance_mn_spent() -> int:
    aggregate = list(
        governance_votes.aggregate(
            [
                {"$group": {"_id": None, "total_mn_spent": {"$sum": "$mn_spent"}}},
            ]
        )
    )
    if not aggregate:
        return 0
    return int(aggregate[0].get("total_mn_spent", 0) or 0)


def _get_active_governance_session(now: datetime) -> dict[str, Any] | None:
    active = governance_sessions.find_one({"status": "active"}, sort=[("started_at", -1)])
    if not active:
        return None

    ends_at = _parse_utc_datetime(active.get("ends_at"))
    if ends_at and ends_at <= now:
        return None

    return active


@router.get("/overview")
@limiter.limit("30/minute")
def get_economy_overview(request: Request, username: str):
    current_user = _sync_user(str(username or "").strip())
    now = _utc_now()

    projected_users = users_collection.find(
        {},
        {
            "username": 1,
            "is_system_account": 1,
            "maze_nuggets": 1,
            "last_seen_at": 1,
            "last_login_at": 1,
            "staked_map_ids": 1,
        },
    )

    player_wallet_mn_total = 0
    bank_reserve_mn_total = 0
    users_total = 0
    users_online_now = 0
    users_active_24h = 0
    stakers_total = 0
    staked_map_ids: set[str] = set()

    for raw_user in projected_users:
        user = apply_user_defaults(raw_user)
        maze_nuggets = int(user.get("maze_nuggets", 0) or 0)

        if _is_system_account(user):
            bank_reserve_mn_total += maze_nuggets
            continue

        users_total += 1
        player_wallet_mn_total += maze_nuggets

        if is_user_online(user, now=now):
            users_online_now += 1
        if _active_within_window(user, now, timedelta(hours=24)):
            users_active_24h += 1

        normalized_staked_map_ids, _ = normalize_staked_map_ids(user, maps_collection)
        if normalized_staked_map_ids:
            stakers_total += 1
            staked_map_ids.update(normalized_staked_map_ids)

    maps_total = maps_collection.count_documents({})
    maps_listed = marketplace_collection.count_documents({})
    maps_staked = len(staked_map_ids)
    listings_total_value, average_listing_price = _get_listing_value_stats()
    governance_mn_spent_total = _get_total_governance_mn_spent()
    active_governance_session = _get_active_governance_session(now)

    maps_staked_pct = round((maps_staked / maps_total) * 100, 2) if maps_total > 0 else 0.0
    maps_listed_pct = round((maps_listed / maps_total) * 100, 2) if maps_total > 0 else 0.0

    return {
        "status": "success",
        "overview": {
            "generated_at": now.isoformat(),
            "mn_player_wallets_total": player_wallet_mn_total,
            "mn_bank_reserve_total": bank_reserve_mn_total,
            "mn_total_known": player_wallet_mn_total + bank_reserve_mn_total,
            "maps_total": int(maps_total),
            "maps_listed": int(maps_listed),
            "maps_staked": int(maps_staked),
            "maps_staked_percent": maps_staked_pct,
            "maps_listed_percent": maps_listed_pct,
            "users_total": int(users_total),
            "users_online_now": int(users_online_now),
            "users_active_24h": int(users_active_24h),
            "stakers_total": int(stakers_total),
            "marketplace_listing_value_total": int(listings_total_value),
            "marketplace_listing_price_avg": round(average_listing_price, 2),
            "governance_voting_open": active_governance_session is not None,
            "governance_active_title": str(active_governance_session.get("title") or "").strip() if active_governance_session else None,
            "governance_total_mn_spent": int(governance_mn_spent_total),
        },
        "user": serialize_session_user(current_user, maps_collection),
    }
