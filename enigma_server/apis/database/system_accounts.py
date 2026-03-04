from __future__ import annotations

import os

import bcrypt
from bson.decimal128 import Decimal128

from .db import users_collection
from .user_utils import SYSTEM_BANK_USERNAME, default_user_fields


def ensure_bank_account() -> None:
    existing = users_collection.find_one({"username": SYSTEM_BANK_USERNAME})
    defaults = default_user_fields(SYSTEM_BANK_USERNAME, "bank@enigma.local")
    password = os.getenv("ENIGMA_BANK_PASSWORD")
    update = {
        "email": "bank@enigma.local",
        "email_normalized": "bank@enigma.local",
        "is_system_account": True,
        "allow_public_profile": False,
    }

    if existing:
        bank_balance = existing.get("maze_nuggets", 0)
        try:
            if isinstance(bank_balance, Decimal128):
                normalized_balance = bank_balance
            else:
                normalized_balance = Decimal128(str(int(bank_balance)))
        except (TypeError, ValueError, ArithmeticError):
            normalized_balance = Decimal128("0")

        password_update: dict[str, str] = {}
        if password:
            existing_hash = existing.get("password")
            existing_hash_bytes = (
                existing_hash.encode("utf-8")
                if isinstance(existing_hash, str)
                else existing_hash
            )
            password_matches = False
            if isinstance(existing_hash_bytes, (bytes, bytearray)):
                try:
                    password_matches = bcrypt.checkpw(password.encode("utf-8"), existing_hash_bytes)
                except ValueError:
                    password_matches = False

            if not password_matches:
                password_update["password"] = bcrypt.hashpw(password.encode("utf-8"), bcrypt.gensalt()).decode("utf-8")

        users_collection.update_one(
            {"_id": existing["_id"]},
            {"$set": {**update, "maze_nuggets": normalized_balance, **password_update}},
        )
        return

    if not password:
        raise RuntimeError("ENIGMA_BANK_PASSWORD must be set before creating the bank account.")

    hashed_password = bcrypt.hashpw(password.encode("utf-8"), bcrypt.gensalt()).decode("utf-8")

    users_collection.insert_one(
        {
            "username": SYSTEM_BANK_USERNAME,
            "email": "bank@enigma.local",
            "email_normalized": "bank@enigma.local",
            "password": hashed_password,
            "maze_nuggets": Decimal128("0"),
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
