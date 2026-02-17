from pymongo.errors import DuplicateKeyError

from .db import item_inventory


ITEMS = [
    {
        "item_id": "flashlight_basic",
        "name": "Basic Flashlight",
        "category": "utility",
        "rarity": "common",
        "price": 50,
        "stock": 25,
        "max_uses": 100,
        "description": "Illuminates dark rooms and hidden corners.",
        "effect": {"type": "vision_boost", "value": 1},
    },
    {
        "item_id": "flashlight_pro",
        "name": "Pro Flashlight",
        "category": "utility",
        "rarity": "rare",
        "price": 140,
        "stock": 10,
        "max_uses": 220,
        "description": "Longer battery life and stronger beam.",
        "effect": {"type": "vision_boost", "value": 2},
    },
    {
        "item_id": "compass_chip",
        "name": "Compass Chip",
        "category": "navigation",
        "rarity": "common",
        "price": 80,
        "stock": 20,
        "max_uses": 15,
        "description": "Points you toward the finish room.",
        "effect": {"type": "direction_hint", "value": 1},
    },
    {
        "item_id": "pathfinder_drone",
        "name": "Pathfinder Drone",
        "category": "navigation",
        "rarity": "epic",
        "price": 320,
        "stock": 6,
        "max_uses": 5,
        "description": "Reveals a short optimal path segment.",
        "effect": {"type": "path_reveal", "value": 4},
    },
    {
        "item_id": "time_freeze",
        "name": "Time Freeze",
        "category": "time",
        "rarity": "rare",
        "price": 180,
        "stock": 12,
        "max_uses": 3,
        "description": "Pauses the completion timer briefly.",
        "effect": {"type": "timer_pause_seconds", "value": 8},
    },
    {
        "item_id": "time_refund",
        "name": "Time Refund",
        "category": "time",
        "rarity": "epic",
        "price": 360,
        "stock": 5,
        "max_uses": 1,
        "description": "Rewinds a few seconds from your run time.",
        "effect": {"type": "timer_rewind_seconds", "value": 20},
    },
    {
        "item_id": "trap_shield",
        "name": "Trap Shield",
        "category": "safety",
        "rarity": "rare",
        "price": 170,
        "stock": 10,
        "max_uses": 2,
        "description": "Absorbs trap effects in dangerous rooms.",
        "effect": {"type": "trap_block", "value": 2},
    },
    {
        "item_id": "puzzle_skip",
        "name": "Puzzle Skip",
        "category": "puzzle",
        "rarity": "rare",
        "price": 210,
        "stock": 8,
        "max_uses": 1,
        "description": "Skips one puzzle room instantly.",
        "effect": {"type": "skip_puzzle", "value": 1},
    },
    {
        "item_id": "hint_pack_small",
        "name": "Hint Pack (Small)",
        "category": "puzzle",
        "rarity": "common",
        "price": 45,
        "stock": 40,
        "max_uses": 3,
        "description": "Provides hints for puzzle interactions.",
        "effect": {"type": "puzzle_hint", "value": 3},
    },
    {
        "item_id": "hint_pack_large",
        "name": "Hint Pack (Large)",
        "category": "puzzle",
        "rarity": "rare",
        "price": 110,
        "stock": 20,
        "max_uses": 10,
        "description": "High-capacity hint bundle for longer runs.",
        "effect": {"type": "puzzle_hint", "value": 10},
    },
    {
        "item_id": "reward_magnet",
        "name": "Reward Magnet",
        "category": "economy",
        "rarity": "epic",
        "price": 400,
        "stock": 4,
        "max_uses": 3,
        "description": "Increases nugget rewards from R rooms.",
        "effect": {"type": "reward_multiplier", "value": 1.25},
    },
    {
        "item_id": "founders_mark",
        "name": "Founder's Mark",
        "category": "cosmetic",
        "rarity": "mythical",
        "price": 0,
        "stock": 5,
        "max_uses": 9999,
        "description": "Rare cosmetic for the first 5 players",
        "effect": {"type": "cosmetic", "value": "founder_glow"},
    },
]


def seed_item_inventory():
    item_inventory.create_index("item_id", unique=True)
    inserted = 0
    for item in ITEMS:
        try:
            item_inventory.insert_one(item)
            inserted += 1
        except DuplicateKeyError:
            continue
    return inserted


if __name__ == "__main__":
    count = seed_item_inventory()
    print(f"Inserted {count} item(s) into item_inventory.")
