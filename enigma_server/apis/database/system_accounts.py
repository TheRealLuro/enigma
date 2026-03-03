from __future__ import annotations

import os

import bcrypt

from .db import users_collection
from .user_utils import SYSTEM_BANK_USERNAME, default_user_fields


def ensure_bank_account() -> None:
    existing = users_collection.find_one({"username": SYSTEM_BANK_USERNAME})
    defaults = default_user_fields(SYSTEM_BANK_USERNAME, "bank@enigma.local")
    update = {
        "email": "bank@enigma.local",
        "email_normalized": "bank@enigma.local",
        "is_system_account": True,
        "allow_public_profile": False,
    }

    if existing:
        users_collection.update_one({"_id": existing["_id"]}, {"$set": update})
        return

    password = os.getenv("ENIGMA_BANK_PASSWORD") or "EnigmaBank!2026#Vault"
    hashed_password = bcrypt.hashpw(password.encode("utf-8"), bcrypt.gensalt()).decode("utf-8")

    users_collection.insert_one(
        {
            "username": SYSTEM_BANK_USERNAME,
            "email": "bank@enigma.local",
            "email_normalized": "bank@enigma.local",
            "password": hashed_password,
            "maze_nuggets": 0,
            "friends": [],
            "friend_requests": [],
            "maps_discovered": [],
            "maps_owned": [],
            "owned_cosmetics": [],
            "item_counts": {},
            "number_of_maps_played": 0,
            "maps_completed": 0,
            "maps_lost": 0,
            **defaults,
        }
    )
