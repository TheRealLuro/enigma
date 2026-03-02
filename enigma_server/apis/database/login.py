from datetime import datetime, timezone

import bcrypt
from fastapi import APIRouter, HTTPException, Request

from main import limiter

from .db import maps_collection, users_collection
from .map_utils import load_maps_by_ids, normalize_int, serialize_user_map_documents

router = APIRouter(prefix="/database/users")


def serialize_login_user(user: dict) -> dict:
    last_login_at = user.get("last_login_at")
    if isinstance(last_login_at, datetime):
        if last_login_at.tzinfo is None:
            last_login_at = last_login_at.replace(tzinfo=timezone.utc)
        last_login_at_value = last_login_at.astimezone(timezone.utc).isoformat()
    else:
        last_login_at_value = None

    owned_map_docs = load_maps_by_ids(user.get("maps_owned", []), maps_collection)
    discovered_map_docs = load_maps_by_ids(user.get("maps_discovered", []), maps_collection)

    return {
        "username": user.get("username", ""),
        "maze_nuggets": normalize_int(user.get("maze_nuggets")),
        "friends": list(user.get("friends", [])),
        "friend_requests": list(user.get("friend_requests", [])),
        "maps_owned": serialize_user_map_documents(owned_map_docs),
        "maps_discovered": serialize_user_map_documents(discovered_map_docs),
        "number_of_maps_played": normalize_int(user.get("number_of_maps_played")),
        "maps_completed": normalize_int(user.get("maps_completed")),
        "maps_lost": normalize_int(user.get("maps_lost")),
        "owned_cosmetics": list(user.get("owned_cosmetics", [])),
        "item_counts": user.get("item_counts", {}),
        "last_login_at": last_login_at_value,
    }


@router.post("/login")
@limiter.limit("2/minute")
def login_user(request: Request, username: str, passwd: str):
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    hashed = user.get("password")
    passwd_bytes = passwd.encode("utf-8")
    hashed_bytes = hashed if isinstance(hashed, bytes) else hashed.encode("utf-8")

    if not bcrypt.checkpw(passwd_bytes, hashed_bytes):
        raise HTTPException(status_code=401, detail="Incorrect password")

    DAILY_REWARD = 50
    now = datetime.now(timezone.utc)
    last_login_at = user.get("last_login_at")

    update = {"$set": {"last_login_at": now}}
    rewarded = False

    if last_login_at is None or last_login_at.date() < now.date():
        update["$inc"] = {"maze_nuggets": DAILY_REWARD}
        rewarded = True

    discovered_ids = list(user.get("maps_discovered", []))
    owned_id_strings = {str(map_id) for map_id in user.get("maps_owned", [])}
    missing_owned_maps = [map_id for map_id in discovered_ids if str(map_id) not in owned_id_strings]
    if missing_owned_maps:
        update["$addToSet"] = {"maps_owned": {"$each": missing_owned_maps}}

    users_collection.update_one({"username": username}, update)

    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    return {
        "status": "success",
        "user": serialize_login_user(user),
        "daily_reward_granted": rewarded,
        "daily_reward_amount": DAILY_REWARD if rewarded else 0,
    }
