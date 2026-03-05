from __future__ import annotations

from copy import deepcopy

from .founders_mark import FOUNDERS_MARK_ITEM_ID


DEFAULT_ITEM_CATALOG = [
    {
        "item_id": "compass_chip",
        "name": "Compass Chip",
        "description": "Points toward the finish room for a brief burst.",
        "kind": "navigation",
        "slot_kind": "support",
        "rarity": "common",
        "price": 80,
        "stock": 20,
        "stackable": False,
        "max_per_run": 1,
        "effect_config": {"type": "direction_hint", "duration_seconds": 10},
        "icon": "compass",
    },
    {
        "item_id": "hint_pack",
        "name": "Hint Pack",
        "description": "Reveals one generated clue for the current puzzle.",
        "kind": "puzzle",
        "slot_kind": "support",
        "rarity": "common",
        "price": 55,
        "stock": 28,
        "stackable": True,
        "max_per_run": 2,
        "effect_config": {"type": "puzzle_hint", "charges": 1},
        "icon": "hint",
    },
    {
        "item_id": "trap_shield",
        "name": "Tactical Compass",
        "description": "Provides an extended directional guide toward the finish room.",
        "kind": "navigation",
        "slot_kind": "support",
        "rarity": "rare",
        "price": 180,
        "stock": 10,
        "stackable": False,
        "max_per_run": 1,
        "effect_config": {"type": "direction_hint", "duration_seconds": 16},
        "icon": "compass",
    },
    {
        "item_id": "reward_magnet",
        "name": "Route Beacon",
        "description": "Reveals the shortest route to your current objective for a longer window.",
        "kind": "navigation",
        "slot_kind": "support",
        "rarity": "epic",
        "price": 320,
        "stock": 6,
        "stackable": False,
        "max_per_run": 1,
        "effect_config": {"type": "path_reveal", "duration_seconds": 16},
        "icon": "drone",
    },
    {
        "item_id": "time_freeze",
        "name": "Time Freeze",
        "description": "Pauses the timer for 20 seconds.",
        "kind": "time",
        "slot_kind": "high_power",
        "rarity": "rare",
        "price": 240,
        "stock": 8,
        "stackable": False,
        "max_per_run": 1,
        "effect_config": {"type": "timer_pause_seconds", "value": 20},
        "icon": "freeze",
    },
    {
        "item_id": "flashlight_pro",
        "name": "Flashlight Pro",
        "description": "Reveals hidden room information for a short window.",
        "kind": "vision",
        "slot_kind": "support",
        "rarity": "rare",
        "price": 140,
        "stock": 14,
        "stackable": False,
        "max_per_run": 1,
        "effect_config": {"type": "vision_boost", "duration_seconds": 12},
        "icon": "flashlight",
    },
    {
        "item_id": "pathfinder_drone",
        "name": "Pathfinder Drone",
        "description": "Highlights the shortest route to the nearest reward or finish target.",
        "kind": "navigation",
        "slot_kind": "support",
        "rarity": "epic",
        "price": 330,
        "stock": 5,
        "stackable": False,
        "max_per_run": 1,
        "effect_config": {"type": "path_reveal", "duration_seconds": 10},
        "icon": "drone",
    },
    {
        "item_id": "puzzle_skip",
        "name": "Puzzle Skip",
        "description": "Completes the current room puzzle instantly.",
        "kind": "puzzle",
        "slot_kind": "high_power",
        "rarity": "rare",
        "price": 260,
        "stock": 7,
        "stackable": False,
        "max_per_run": 1,
        "effect_config": {"type": "skip_puzzle", "charges": 1},
        "icon": "skip",
    },
    {
        "item_id": FOUNDERS_MARK_ITEM_ID,
        "name": "Founders Mark",
        "description": "Beta tester legacy item. Permanently boosts Maze Nuggets earned by 50 percent and halves gameplay dividend fees.",
        "kind": "economy",
        "category": "perk",
        "slot_kind": "perk",
        "rarity": "founder",
        "price": 0,
        "stock": 10,
        "stackable": False,
        "max_per_run": 0,
        "effect_config": {"type": "permanent_founder_bonus", "reward_multiplier": 1.5, "fee_multiplier": 0.5},
        "icon": "founder",
        "always_available": True,
        "never_restock": True,
        "purchase_limit": 1,
        "requires_tasks": True,
    },
]

# Patch legacy DB records in-place at serialization time so old non-functional items
# become usable without requiring an immediate backfill script.
LEGACY_ITEM_RUNTIME_PATCHES = {
    "trap_shield": {
        "name": "Tactical Compass",
        "description": "Provides an extended directional guide toward the finish room.",
        "kind": "navigation",
        "category": "navigation",
        "slot_kind": "support",
        "stackable": False,
        "max_per_run": 1,
        "max_uses": 1,
        "effect_config": {"type": "direction_hint", "duration_seconds": 16},
        "effect": {"type": "direction_hint", "duration_seconds": 16},
        "icon": "compass",
    },
    "reward_magnet": {
        "name": "Route Beacon",
        "description": "Reveals the shortest route to your current objective for a longer window.",
        "kind": "navigation",
        "category": "navigation",
        "slot_kind": "support",
        "stackable": False,
        "max_per_run": 1,
        "max_uses": 1,
        "effect_config": {"type": "path_reveal", "duration_seconds": 16},
        "effect": {"type": "path_reveal", "duration_seconds": 16},
        "icon": "drone",
    },
}

SUPPORTED_ACTIVE_EFFECT_TYPES = {
    "direction_hint",
    "puzzle_hint",
    "timer_pause_seconds",
    "vision_boost",
    "path_reveal",
    "skip_puzzle",
}
SUPPORTED_PASSIVE_EFFECT_TYPES = {
    "reward_multiplier",
}


def normalize_item_doc(item: dict) -> dict:
    normalized = deepcopy(item)
    item_id = str(normalized.get("item_id") or "").strip().lower()
    legacy_patch = LEGACY_ITEM_RUNTIME_PATCHES.get(item_id)
    if legacy_patch:
        normalized.update(deepcopy(legacy_patch))

    kind = normalized.get("kind") or normalized.get("category") or "utility"
    slot_kind = normalized.get("slot_kind") or infer_slot_kind(kind, normalized.get("item_id"))
    effect_config = normalized.get("effect_config") or normalized.get("effect") or {}
    max_per_run = int(normalized.get("max_per_run") or normalized.get("max_uses") or 1)
    stackable = bool(normalized.get("stackable", max_per_run > 1))

    normalized["kind"] = kind
    normalized["category"] = normalized.get("category") or kind
    normalized["slot_kind"] = slot_kind
    normalized["stackable"] = stackable
    normalized["max_per_run"] = max_per_run
    normalized["max_uses"] = max_per_run
    normalized["effect_config"] = effect_config
    normalized["effect"] = effect_config
    normalized["icon"] = normalized.get("icon")
    normalized["price"] = int(normalized.get("price", 0) or 0)
    normalized["stock"] = int(normalized.get("stock", 0) or 0)
    normalized["rarity"] = str(normalized.get("rarity") or "common")
    normalized["always_available"] = bool(normalized.get("always_available", False))
    normalized["never_restock"] = bool(normalized.get("never_restock", False))
    normalized["purchase_limit"] = int(normalized.get("purchase_limit", 0) or 0)
    normalized["requires_tasks"] = bool(normalized.get("requires_tasks", False))
    return normalized


def is_item_supported_for_current_app(item: dict) -> bool:
    normalized = normalize_item_doc(item)
    slot_kind = str(normalized.get("slot_kind") or "").strip().lower()
    effect_type = str((normalized.get("effect_config") or {}).get("type") or "").strip().lower()

    if slot_kind == "perk":
        return True

    if not effect_type:
        return False

    if slot_kind == "passive":
        return effect_type in SUPPORTED_PASSIVE_EFFECT_TYPES

    return effect_type in SUPPORTED_ACTIVE_EFFECT_TYPES


def serialize_shop_item(item: dict) -> dict:
    normalized = normalize_item_doc(item)
    normalized.pop("_id", None)
    return {
        "item_id": normalized.get("item_id", ""),
        "name": normalized.get("name", normalized.get("item_id", "")),
        "description": normalized.get("description", ""),
        "kind": normalized.get("kind", "utility"),
        "category": normalized.get("category", normalized.get("kind", "utility")),
        "slot_kind": normalized.get("slot_kind", "support"),
        "rarity": normalized.get("rarity", "common"),
        "price": int(normalized.get("price", 0) or 0),
        "stock": int(normalized.get("stock", 0) or 0),
        "stackable": bool(normalized.get("stackable", True)),
        "max_per_run": int(normalized.get("max_per_run", 1) or 1),
        "effect_config": normalized.get("effect_config") or {},
        "icon": normalized.get("icon"),
        "always_available": bool(normalized.get("always_available", False)),
        "never_restock": bool(normalized.get("never_restock", False)),
        "purchase_limit": int(normalized.get("purchase_limit", 0) or 0),
        "requires_tasks": bool(normalized.get("requires_tasks", False)),
    }


def infer_slot_kind(kind: str, item_id: str | None = None) -> str:
    key = str(item_id or "").lower()
    category = str(kind or "").lower()
    if key == FOUNDERS_MARK_ITEM_ID:
        return "perk"
    if key in {"time_freeze", "puzzle_skip"}:
        return "high_power"
    if category in {"passive", "high_power", "support"}:
        return category
    return "support"
