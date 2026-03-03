from __future__ import annotations

from typing import Any

from bson import ObjectId
from fastapi import HTTPException

from .db import maps_collection, marketplace_collection, run_results, users_collection
from .user_utils import SYSTEM_BANK_USERNAME, apply_user_defaults


def _owned_map_documents_for_user(username: str) -> list[dict[str, Any]]:
    username = (username or "").strip()
    if not username:
        return []

    return list(maps_collection.find({"owner": username}))


def delete_user_account(username: str) -> dict[str, int]:
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    normalized_user = apply_user_defaults(user)
    if normalized_user.get("is_system_account") or normalized_user.get("username") == SYSTEM_BANK_USERNAME:
        raise HTTPException(status_code=403, detail="System accounts cannot be deleted")

    owned_maps = _owned_map_documents_for_user(username)
    owned_map_ids = [map_doc.get("_id") for map_doc in owned_maps if isinstance(map_doc.get("_id"), ObjectId)]
    owned_map_names = [str(map_doc.get("map_name")) for map_doc in owned_maps if map_doc.get("map_name")]

    if owned_map_ids:
        users_collection.update_many(
            {},
            {
                "$pull": {
                    "maps_owned": {"$in": owned_map_ids},
                    "maps_discovered": {"$in": owned_map_ids},
                }
            },
        )

    if owned_map_names:
        marketplace_collection.delete_many({"$or": [{"seller": username}, {"map_name": {"$in": owned_map_names}}]})
        users_collection.update_many(
            {"profile_image.map_name": {"$in": owned_map_names}},
            {"$set": {"profile_image": None}},
        )
    else:
        marketplace_collection.delete_many({"seller": username})

    users_collection.update_many(
        {},
        {
            "$pull": {
                "friends": username,
                "friend_requests": username,
            }
        },
    )

    if owned_map_ids:
        maps_collection.delete_many({"_id": {"$in": owned_map_ids}})

    run_results.delete_many({"username": username})
    users_collection.delete_one({"username": username})

    return {
        "deleted_maps": len(owned_map_ids),
        "deleted_marketplace_listings": len(owned_map_names),
    }
