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
ANALYSIS_FULL_HOURS = 72
ANALYSIS_FULL_SECONDS = ANALYSIS_FULL_HOURS * 3600
CONTAINMENT_SECONDS = STAKED_MAP_LOCK_HOURS * 3600
RESEARCH_DATA_MAX_SECONDS = 14 * 24 * 3600
VOTE_POWER_MAX_MULTIPLIER = 2.25
VOTE_STAKE_CURVE_SCALE = 0.22
VOTE_STAKE_BONUS_CAP = 0.95
VOTE_PARTICIPATION_STEP = 0.01
VOTE_PARTICIPATION_BONUS_CAP = 0.20
RESEARCH_DEPTH_LEVELS: list[tuple[int, str]] = [
    (48 * 3600, "Surface Scan"),
    (72 * 3600, "Structural Mapping"),
    (7 * 24 * 3600, "Puzzle Pattern Analysis"),
    (14 * 24 * 3600, "Dimensional Core Study"),
]
ANALYSIS_UNLOCK_MILESTONES: list[tuple[int, str, str]] = [
    (1 * 3600, "basic_map_data", "Basic map data extracted"),
    (6 * 3600, "pattern_insights", "Puzzle pattern insights decoded"),
    (24 * 3600, "anomaly_secrets", "Anomaly secrets identified"),
    (72 * 3600, "rare_artifact", "Rare artifact signature recovered"),
]
ANOMALY_TYPE_LABELS = [
    "Recursive Labyrinth",
    "Fractal Corridor",
    "Echoing Vault",
    "Phase-Shift Array",
    "Black Labyrinth",
]
PUZZLE_STRUCTURE_LABELS = [
    "Symbolic Logic",
    "Directional Echo",
    "Signal Routing",
    "Weighted Recursion",
    "Temporal Sequence",
]


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


def _stable_hash(value: str) -> int:
    hash_value = 2166136261
    for character in value:
        hash_value ^= ord(character)
        hash_value = (hash_value * 16777619) & 0xFFFFFFFF
    return hash_value & 0x7FFFFFFF


def stake_lock_expires_at(staked_at: datetime | None = None) -> datetime:
    current = staked_at or _utc_now()
    if current.tzinfo is None:
        current = current.replace(tzinfo=timezone.utc)
    return current.astimezone(timezone.utc) + timedelta(hours=STAKED_MAP_LOCK_HOURS)


def _analysis_phase_for_progress(progress_ratio: float) -> tuple[str, str]:
    normalized = max(0.0, min(1.0, float(progress_ratio or 0.0)))
    if normalized < 1.0:
        return "initial_containment", "Initial Containment"
    return "continuous_research", "Continuous Research"


def _research_depth_for_elapsed(elapsed_seconds: int) -> tuple[int, str]:
    depth_level = 0
    depth_label = "Containment Pending"
    for index, (threshold, label) in enumerate(RESEARCH_DEPTH_LEVELS, start=1):
        if elapsed_seconds >= threshold:
            depth_level = index
            depth_label = label
    return depth_level, depth_label


def _analysis_ai_message(phase_key: str, progress_percent: float) -> str:
    if phase_key == "initial_containment":
        return "ECHO: Containment lattice active. Stabilizing dimensional structure."
    if progress_percent >= 100.0:
        return "ECHO: Deep analysis complete. Intent signatures remain unresolved."
    return "ECHO: Continuous research active. Decoding anomaly behavior patterns."


def _anomaly_classification(map_doc: dict[str, Any]) -> str:
    difficulty = normalize_difficulty(map_doc.get("difficulty"))
    size = max(1, normalize_int(map_doc.get("size"), 1))
    if difficulty == "hard" or size >= 12:
        return "Labyrinth Type V - Dimensional Fracture"
    if difficulty == "medium" or size >= 6:
        return "Labyrinth Type III - Recursive"
    return "Labyrinth Type I - Simple"


def _analysis_unlocks(started_at: datetime, elapsed_seconds: int) -> tuple[list[dict[str, Any]], str]:
    unlocks: list[dict[str, Any]] = []
    deepest_label = "No milestones unlocked yet"

    for threshold_seconds, unlock_key, unlock_label in ANALYSIS_UNLOCK_MILESTONES:
        unlocked = elapsed_seconds >= threshold_seconds
        if unlocked:
            deepest_label = unlock_label
        unlocks.append(
            {
                "key": unlock_key,
                "label": unlock_label,
                "required_hours": round(threshold_seconds / 3600.0, 2),
                "unlocked": unlocked,
                "unlock_at": (started_at + timedelta(seconds=threshold_seconds)).isoformat(),
            }
        )

    return unlocks, deepest_label


def _analysis_report(map_doc: dict[str, Any], elapsed_seconds: int) -> dict[str, Any] | None:
    if elapsed_seconds < ANALYSIS_FULL_SECONDS:
        return None

    map_id = str(map_doc.get("_id") or "")
    map_name = str(map_doc.get("map_name") or "Unnamed Map")
    difficulty = normalize_difficulty(map_doc.get("difficulty"))
    size = max(1, normalize_int(map_doc.get("size"), 1))
    seed = f"{map_id}:{map_name}:{difficulty}:{size}"
    seed_hash = _stable_hash(seed)
    anomaly_type = ANOMALY_TYPE_LABELS[seed_hash % len(ANOMALY_TYPE_LABELS)]
    puzzle_structure = PUZZLE_STRUCTURE_LABELS[(seed_hash // 7) % len(PUZZLE_STRUCTURE_LABELS)]
    stability_rating = 55 + (seed_hash % 41)
    classified = (difficulty == "hard" and size >= 10) or (seed_hash % 17 == 0)
    status = "Restricted Research" if classified else "Open Research"
    report_id = f"EXP-{seed_hash % 100000:05d}"
    conclusion = (
        "The anomaly appears intentionally constructed. Further study required."
        if stability_rating < 80
        else "Structure is stable enough for replication in future expedition templates."
    )

    return {
        "report_id": report_id,
        "anomaly_type": anomaly_type,
        "puzzle_structure": puzzle_structure,
        "stability_rating": int(stability_rating),
        "status": status,
        "conclusion": conclusion,
    }


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


def normalize_staked_map_started_at(
    user: dict[str, Any],
    staked_map_ids: list[str],
    lock_lookup: dict[str, str] | None = None,
) -> dict[str, str]:
    raw_lookup = user.get("staked_map_started_at") if isinstance(user.get("staked_map_started_at"), dict) else {}
    normalized: dict[str, str] = {}
    current = _utc_now()
    lock_values = lock_lookup or {}

    for staked_map_id in staked_map_ids:
        parsed = _parse_utc_datetime(raw_lookup.get(staked_map_id))
        if parsed is None:
            lock_parsed = _parse_utc_datetime(lock_values.get(staked_map_id))
            if lock_parsed is not None:
                parsed = lock_parsed - timedelta(hours=STAKED_MAP_LOCK_HOURS)

        if parsed is None:
            parsed = current

        normalized[staked_map_id] = parsed.isoformat()

    return normalized


def serialize_staked_map(
    map_doc: dict[str, Any],
    lock_until: datetime | None = None,
    started_at: datetime | None = None,
    now: datetime | None = None,
    under_analysis: bool = False,
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
    analysis_start = started_at.astimezone(timezone.utc) if isinstance(started_at, datetime) else current
    elapsed_seconds = max(0, int((current - analysis_start).total_seconds())) if under_analysis else 0
    report_progress_ratio = min(1.0, elapsed_seconds / ANALYSIS_FULL_SECONDS) if under_analysis else 0.0
    progress_percent = round(report_progress_ratio * 100.0, 2)
    containment_progress_percent = (
        round(min(100.0, (elapsed_seconds / max(1, CONTAINMENT_SECONDS)) * 100.0), 2)
        if under_analysis
        else 0.0
    )
    phase_key, phase_label = _analysis_phase_for_progress(0.0 if is_locked else 1.0)
    eta_seconds = max(0, ANALYSIS_FULL_SECONDS - elapsed_seconds) if under_analysis else ANALYSIS_FULL_SECONDS
    unlocks, deepest_unlock = _analysis_unlocks(analysis_start, elapsed_seconds) if under_analysis else ([], "Not submitted for analysis")
    report = _analysis_report(map_doc, elapsed_seconds) if under_analysis else None
    depth_level, depth_label = _research_depth_for_elapsed(elapsed_seconds) if under_analysis else (0, "Not submitted")
    data_percent = (
        round(min(100.0, (elapsed_seconds / max(1, RESEARCH_DATA_MAX_SECONDS)) * 100.0), 2)
        if under_analysis
        else 0.0
    )
    seed = _stable_hash(f"{map_doc.get('_id')}:{map_doc.get('map_name')}")
    puzzle_structures_identified = (
        max(1, int(data_percent // 8) + 1 + (seed % 2))
        if under_analysis
        else 0
    )
    spatial_layers_detected = (
        max(1, int(data_percent // 25) + 1 + (seed % 2))
        if under_analysis
        else 0
    )

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
        "analysis_started_at": analysis_start.isoformat() if under_analysis else None,
        "analysis_elapsed_seconds": elapsed_seconds,
        "analysis_progress_percent": progress_percent,
        "analysis_phase_key": phase_key if under_analysis else "not_submitted",
        "analysis_phase_label": phase_label if under_analysis else "Not submitted",
        "containment_active": bool(under_analysis and is_locked),
        "containment_remaining_seconds": lock_seconds_remaining if under_analysis else 0,
        "containment_progress_percent": containment_progress_percent,
        "research_depth_level": depth_level,
        "research_depth_label": depth_label,
        "research_data_percent": data_percent,
        "puzzle_structures_identified": puzzle_structures_identified,
        "spatial_layers_detected": spatial_layers_detected,
        "anomaly_classification": _anomaly_classification(map_doc),
        "analysis_eta_seconds": eta_seconds,
        "analysis_eta_at": (current + timedelta(seconds=eta_seconds)).isoformat() if under_analysis and eta_seconds > 0 else None,
        "analysis_ai_message": _analysis_ai_message(phase_key, progress_percent) if under_analysis else "ECHO: Awaiting map submission.",
        "analysis_unlocks": unlocks,
        "analysis_deepest_unlock_label": deepest_unlock,
        "analysis_report": report,
    }


def build_staking_overview(user: dict[str, Any], maps_collection, now: datetime | None = None) -> dict[str, Any]:
    current = now or _utc_now()
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    normalized_started_lookup = normalize_staked_map_started_at(user, normalized_staked_ids, normalized_lock_lookup)
    staked_id_set = set(normalized_staked_ids)

    staked_maps = [
        serialize_staked_map(
            owned_lookup[map_id],
            lock_until=_parse_utc_datetime(normalized_lock_lookup.get(map_id)),
            started_at=_parse_utc_datetime(normalized_started_lookup.get(map_id)),
            now=current,
            under_analysis=True,
        )
        for map_id in normalized_staked_ids
        if map_id in owned_lookup
    ]
    available_maps = [
        serialize_staked_map(map_doc, now=current, under_analysis=False)
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
        "analysis_full_hours": ANALYSIS_FULL_HOURS,
    }


def claim_daily_staking_reward(users_collection, maps_collection, user: dict[str, Any], now: datetime | None = None) -> dict[str, Any]:
    current = now or _utc_now()
    normalized_staked_ids, owned_lookup = normalize_staked_map_ids(user, maps_collection)
    normalized_lock_lookup = normalize_staked_map_lock_until(user, normalized_staked_ids)
    normalized_started_lookup = normalize_staked_map_started_at(user, normalized_staked_ids, normalized_lock_lookup)
    current_staked_ids = [str(value or "").strip() for value in list(user.get("staked_map_ids", []) or []) if str(value or "").strip()]
    current_lock_lookup = user.get("staked_map_lock_until") if isinstance(user.get("staked_map_lock_until"), dict) else {}
    current_started_lookup = user.get("staked_map_started_at") if isinstance(user.get("staked_map_started_at"), dict) else {}
    needs_stake_cleanup = normalized_staked_ids != current_staked_ids
    needs_lock_cleanup = normalized_lock_lookup != current_lock_lookup
    needs_started_cleanup = normalized_started_lookup != current_started_lookup

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
        if needs_stake_cleanup or needs_lock_cleanup or needs_started_cleanup:
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
        return {
            "rewarded_mn": 0,
            "reward_granted": False,
            "staked_map_ids": normalized_staked_ids,
            "staked_map_lock_until": normalized_lock_lookup,
            "staked_map_started_at": normalized_started_lookup,
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
    if needs_started_cleanup:
        set_update["staked_map_started_at"] = normalized_started_lookup

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
            "staked_map_started_at": normalized_started_lookup,
            "last_claim_at": last_claim_at.isoformat() if last_claim_at else None,
        }

    return {
        "rewarded_mn": reward_total,
        "reward_granted": True,
        "staked_map_ids": normalized_staked_ids,
        "staked_map_lock_until": normalized_lock_lookup,
        "staked_map_started_at": normalized_started_lookup,
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
