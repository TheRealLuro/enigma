from __future__ import annotations

from datetime import datetime, timedelta, timezone
from decimal import Decimal
import threading
import time
from typing import Any

from bson.decimal128 import Decimal128
from fastapi import APIRouter, HTTPException, Request

from main import limiter

from .db import governance_sessions, governance_votes, maps_collection, marketplace_collection, users_collection
from .map_utils import normalize_int
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    apply_user_defaults,
    is_user_online,
    serialize_session_user,
)

router = APIRouter(prefix="/database/economy")
ECONOMY_OVERVIEW_CACHE_TTL_SECONDS = 5.0
_economy_cache_lock = threading.Lock()
_economy_cache_expires_at = 0.0
_economy_cached_overview: dict[str, Any] | None = None


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


def _load_user_or_404(username: str) -> dict[str, Any]:
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")
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


def _normalize_float(value: Any, default: float = 0.0) -> float:
    if isinstance(value, Decimal128):
        try:
            value = value.to_decimal()
        except (ArithmeticError, ValueError):
            return default

    if isinstance(value, Decimal):
        try:
            parsed = float(value)
        except (ArithmeticError, ValueError):
            return default
    else:
        try:
            parsed = float(value)
        except (TypeError, ValueError):
            return default

    if parsed != parsed:  # NaN guard
        return default
    return parsed


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
    return (
        normalize_int(aggregate[0].get("total_value", 0), 0),
        _normalize_float(aggregate[0].get("avg_value", 0.0), 0.0),
    )


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
    return normalize_int(aggregate[0].get("total_mn_spent", 0), 0)


def _get_active_governance_session(now: datetime) -> dict[str, Any] | None:
    active = governance_sessions.find_one({"status": "active"}, sort=[("started_at", -1)])
    if not active:
        return None

    ends_at = _parse_utc_datetime(active.get("ends_at"))
    if ends_at and ends_at <= now:
        return None

    return active


def _compute_overview_snapshot(now: datetime) -> dict[str, Any]:
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
        maze_nuggets = normalize_int(user.get("maze_nuggets", 0), 0)

        if _is_system_account(user):
            bank_reserve_mn_total += maze_nuggets
            continue

        users_total += 1
        player_wallet_mn_total += maze_nuggets

        if is_user_online(user, now=now):
            users_online_now += 1
        if _active_within_window(user, now, timedelta(hours=24)):
            users_active_24h += 1

        raw_staked_ids = {
            str(map_id).strip()
            for map_id in list(user.get("staked_map_ids", []) or [])
            if str(map_id).strip()
        }
        if raw_staked_ids:
            stakers_total += 1
            staked_map_ids.update(raw_staked_ids)

    maps_total = maps_collection.count_documents({})
    maps_listed = marketplace_collection.count_documents({})
    maps_staked = len(staked_map_ids)
    listings_total_value, average_listing_price = _get_listing_value_stats()
    governance_mn_spent_total = _get_total_governance_mn_spent()
    active_governance_session = _get_active_governance_session(now)

    maps_staked_pct = round((maps_staked / maps_total) * 100, 2) if maps_total > 0 else 0.0
    maps_listed_pct = round((maps_listed / maps_total) * 100, 2) if maps_total > 0 else 0.0

    return {
        "generated_at": now.isoformat(),
        "mn_player_wallets_total": player_wallet_mn_total,
        "mn_bank_reserve_total": bank_reserve_mn_total,
        "mn_total_known": player_wallet_mn_total + bank_reserve_mn_total,
        "maps_total": normalize_int(maps_total, 0),
        "maps_listed": normalize_int(maps_listed, 0),
        "maps_staked": normalize_int(maps_staked, 0),
        "maps_staked_percent": maps_staked_pct,
        "maps_listed_percent": maps_listed_pct,
        "users_total": normalize_int(users_total, 0),
        "users_online_now": normalize_int(users_online_now, 0),
        "users_active_24h": normalize_int(users_active_24h, 0),
        "stakers_total": normalize_int(stakers_total, 0),
        "marketplace_listing_value_total": normalize_int(listings_total_value, 0),
        "marketplace_listing_price_avg": round(average_listing_price, 2),
        "governance_voting_open": active_governance_session is not None,
        "governance_active_title": str(active_governance_session.get("title") or "").strip() if active_governance_session else None,
        "governance_total_mn_spent": normalize_int(governance_mn_spent_total, 0),
    }


def _get_cached_overview_snapshot() -> dict[str, Any]:
    global _economy_cache_expires_at, _economy_cached_overview

    now_monotonic = time.monotonic()
    if _economy_cached_overview and now_monotonic < _economy_cache_expires_at:
        return _economy_cached_overview

    with _economy_cache_lock:
        now_monotonic = time.monotonic()
        if _economy_cached_overview and now_monotonic < _economy_cache_expires_at:
            return _economy_cached_overview

        computed_now = _utc_now()
        _economy_cached_overview = _compute_overview_snapshot(computed_now)
        _economy_cache_expires_at = now_monotonic + ECONOMY_OVERVIEW_CACHE_TTL_SECONDS
        return _economy_cached_overview


@router.get("/overview")
@limiter.limit("30/minute")
def get_economy_overview(request: Request, username: str):
    current_user = _load_user_or_404(str(username or "").strip())
    overview_payload = dict(_get_cached_overview_snapshot())

    return {
        "status": "success",
        "overview": overview_payload,
        "user": serialize_session_user(current_user, maps_collection, include_maps=False),
    }
