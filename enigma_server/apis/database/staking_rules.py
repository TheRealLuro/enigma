from __future__ import annotations

import math
from datetime import datetime, timedelta, timezone
from typing import Any

from .map_utils import normalize_int

DAILY_STAKE_BASE_BY_DIFFICULTY = {
    "easy": 5,
    "medium": 15,
    "hard": 25,
}
SIZE_MULTIPLIER_CAP = 2.0
STAKED_MAP_LOCK_HOURS = 48
VOTE_POWER_MAX_MULTIPLIER = 2.25
VOTE_STAKE_CURVE_SCALE = 0.22
VOTE_STAKE_BONUS_CAP = 0.95
VOTE_PARTICIPATION_STEP = 0.01
VOTE_PARTICIPATION_BONUS_CAP = 0.20


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


def stake_lock_expires_at(staked_at: datetime | None = None) -> datetime:
    current = staked_at or _utc_now()
    if current.tzinfo is None:
        current = current.replace(tzinfo=timezone.utc)
    return current.astimezone(timezone.utc) + timedelta(hours=STAKED_MAP_LOCK_HOURS)


def normalize_difficulty(value: Any) -> str:
    normalized = str(value or "").strip().lower()
    if normalized in DAILY_STAKE_BASE_BY_DIFFICULTY:
        return normalized
    return "easy"


def base_stake_reward_for_difficulty(difficulty: Any) -> int:
    return DAILY_STAKE_BASE_BY_DIFFICULTY[normalize_difficulty(difficulty)]


def size_multiplier_for_size(size: Any) -> float:
    normalized_size = max(1, normalize_int(size, 1))
    # Targets requested: 2x2 ~= 0.8x, 4x4 ~= 0.9x
    multiplier = 0.7 + (normalized_size / 20.0)
    return round(min(SIZE_MULTIPLIER_CAP, multiplier), 4)


def daily_stake_reward_for_map(map_doc: dict[str, Any]) -> int:
    base = base_stake_reward_for_difficulty(map_doc.get("difficulty"))
    multiplier = size_multiplier_for_size(map_doc.get("size"))
    return int(math.ceil(base * multiplier))


def _load_owned_map_lookup(user: dict[str, Any], maps_collection) -> dict[str, dict[str, Any]]:
    username = str(user.get("username") or "").strip()
    if not username:
        return {}

    owned_docs = list(
        maps_collection.find(
            {"owner": username},
            {
                "_id": 1,
                "map_name": 1,
                "difficulty": 1,
                "size": 1,
            },
        )
    )
    return {str(doc["_id"]): doc for doc in owned_docs}


def normalize_staked_map_ids(user: dict[str, Any], maps_collection) -> tuple[list[str], dict[str, dict[str, Any]]]:
    owned_lookup = _load_owned_map_lookup(user, maps_collection)
    owned_ids = set(owned_lookup.keys())
    dedupe: set[str] = set()
    normalized: list[str] = []
    for value in list(user.get("staked_map_ids", []) or []):
        map_id = str(value or "").strip()
        if not map_id or map_id in dedupe or map_id not in owned_ids:
            continue
        dedupe.add(map_id)
        normalized.append(map_id)
    return normalized, owned_lookup


def normalize_staked_map_lock_until(user: dict[str, Any], staked_map_ids: list[str]) -> dict[str, str]:
    raw_lookup = user.get("staked_map_lock_until") if isinstance(user.get("staked_map_lock_until"), dict) else {}
    normalized: dict[str, str] = {}

    for staked_map_id in staked_map_ids:
        parsed = _parse_utc_datetime(raw_lookup.get(staked_map_id))
        if parsed is None:
            continue
        normalized[staked_map_id] = parsed.isoformat()

    return normalized


def serialize_staked_map(
    map_doc: dict[str, Any],
    lock_until: datetime | None = None,
    now: datetime | None = None,
) -> dict[str, Any]:
    difficulty = normalize_difficulty(map_doc.get("difficulty"))
    size = max(1, normalize_int(map_doc.get("size"), 1))
    base_reward = base_stake_reward_for_difficulty(difficulty)
    size_multiplier = size_multiplier_for_size(size)
    daily_reward = int(math.ceil(base_reward * size_multiplier))
    current = now or _utc_now()
    lock_end = lock_until.astimezone(timezone.utc) if isinstance(lock_until, datetime) else None
    is_locked = bool(lock_end and lock_end > current)
    lock_seconds_remaining = max(0, int((lock_end - current).total_seconds())) if is_locked else 0
    return {
        "map_id": str(map_doc.get("_id") or ""),
        "map_name": str(map_doc.get("map_name") or "Unnamed Map"),
        "difficulty": difficulty,
        "size": size,
        "base_reward": base_reward,
        "size_multiplier": size_multiplier,
        "daily_reward": daily_reward,
        "locked_until": lock_end.isoformat() if lock_end else None,
        "is_locked": is_locked,
        "lock_seconds_remaining": lock_seconds_remaining,
    }


def build_staking_overview(user: dict[str, Any], maps_collection, now: datetime | None = None) -> dict[str, Any]:
    current = now or _utc_now()
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    staked_id_set = set(normalized_staked_ids)

    staked_maps = [
        serialize_staked_map(
            owned_lookup[map_id],
            lock_until=_parse_utc_datetime(normalized_lock_lookup.get(map_id)),
            now=current,
        )
        for map_id in normalized_staked_ids
        if map_id in owned_lookup
    ]
    available_maps = [
        serialize_staked_map(map_doc)
        for map_id, map_doc in owned_lookup.items()
        if map_id not in staked_id_set
    ]
    available_maps.sort(key=lambda entry: str(entry.get("map_name") or "").lower())

    last_claim_at = _parse_utc_datetime(user.get("last_staking_reward_at"))
    can_claim_today = bool(staked_maps) and (
        last_claim_at is None or last_claim_at.date() < current.date()
    )
    next_claim_at = (last_claim_at + timedelta(days=1)).isoformat() if last_claim_at else None

    return {
        "staked_maps": staked_maps,
        "available_maps": available_maps,
        "staked_maps_count": len(staked_maps),
        "available_maps_count": len(available_maps),
        "total_daily_reward": int(sum(int(entry.get("daily_reward", 0) or 0) for entry in staked_maps)),
        "last_claim_at": last_claim_at.isoformat() if last_claim_at else None,
        "next_claim_at": next_claim_at,
        "can_claim_today": can_claim_today,
        "stake_lock_hours": STAKED_MAP_LOCK_HOURS,
    }


def claim_daily_staking_reward(users_collection, maps_collection, user: dict[str, Any], now: datetime | None = None) -> dict[str, Any]:
    current = now or _utc_now()
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    current_staked_ids = [str(value or "").strip() for value in list(user.get("staked_map_ids", []) or []) if str(value or "").strip()]
    current_lock_lookup = user.get("staked_map_lock_until") if isinstance(user.get("staked_map_lock_until"), dict) else {}
    needs_stake_cleanup = normalized_staked_ids != current_staked_ids
    needs_lock_cleanup = normalized_lock_lookup != current_lock_lookup

    last_claim_at = _parse_utc_datetime(user.get("last_staking_reward_at"))
    claim_due = last_claim_at is None or last_claim_at.date() < current.date()
    reward_total = int(
        sum(
            daily_stake_reward_for_map(owned_lookup[map_id])
            for map_id in normalized_staked_ids
            if map_id in owned_lookup
        )
    )

    if not claim_due or reward_total <= 0:
        if needs_stake_cleanup or needs_lock_cleanup:
            users_collection.update_one(
                {"_id": user["_id"]},
                {"$set": {"staked_map_ids": normalized_staked_ids, "staked_map_lock_until": normalized_lock_lookup}},
            )
        return {
            "rewarded_mn": 0,
            "reward_granted": False,
            "staked_map_ids": normalized_staked_ids,
            "staked_map_lock_until": normalized_lock_lookup,
            "last_claim_at": last_claim_at.isoformat() if last_claim_at else None,
        }

    start_of_day = datetime(current.year, current.month, current.day, tzinfo=timezone.utc)
    update_filter = {
        "_id": user["_id"],
        "$or": [
            {"last_staking_reward_at": {"$exists": False}},
            {"last_staking_reward_at": None},
            {"last_staking_reward_at": {"$lt": start_of_day}},
            {"last_staking_reward_at": {"$type": "string"}},
        ],
    }
    set_update: dict[str, Any] = {"last_staking_reward_at": current}
    if needs_stake_cleanup:
        set_update["staked_map_ids"] = normalized_staked_ids
    if needs_lock_cleanup:
        set_update["staked_map_lock_until"] = normalized_lock_lookup

    update_result = users_collection.update_one(
        update_filter,
        {
            "$set": set_update,
            "$inc": {"maze_nuggets": reward_total},
        },
    )
    if update_result.modified_count != 1:
        return {
            "rewarded_mn": 0,
            "reward_granted": False,
            "staked_map_ids": normalized_staked_ids,
            "last_claim_at": last_claim_at.isoformat() if last_claim_at else None,
        }

    return {
        "rewarded_mn": reward_total,
        "reward_granted": True,
        "staked_map_ids": normalized_staked_ids,
        "staked_map_lock_until": normalized_lock_lookup,
        "last_claim_at": current.isoformat(),
    }


def vote_weight_multiplier(staked_maps_count: int) -> float:
    return vote_multiplier_breakdown(staked_maps_count)["effective_multiplier"]


def vote_multiplier_breakdown(staked_maps_count: int, participation_votes_count: int = 0) -> dict[str, float | int]:
    stake_count = max(0, int(staked_maps_count or 0))
    effective_stake_count = max(1, stake_count)
    sqrt_stake = math.sqrt(effective_stake_count)

    stake_bonus = max(0.0, (sqrt_stake - 1.0) * VOTE_STAKE_CURVE_SCALE)
    stake_multiplier = 1.0 + min(VOTE_STAKE_BONUS_CAP, stake_bonus)

    participation_count = max(0, int(participation_votes_count or 0))
    participation_bonus = min(VOTE_PARTICIPATION_BONUS_CAP, participation_count * VOTE_PARTICIPATION_STEP)
    participation_multiplier = 1.0 + participation_bonus

    raw_multiplier = stake_multiplier * participation_multiplier
    effective_multiplier = min(VOTE_POWER_MAX_MULTIPLIER, raw_multiplier)

    return {
        "staked_maps_count": stake_count,
        "participation_votes_count": participation_count,
        "sqrt_stake": round(sqrt_stake, 6),
        "stake_multiplier": round(stake_multiplier, 4),
        "participation_multiplier": round(participation_multiplier, 4),
        "raw_multiplier": round(raw_multiplier, 4),
        "effective_multiplier": round(effective_multiplier, 4),
        "multiplier_cap": round(VOTE_POWER_MAX_MULTIPLIER, 4),
    }
