from datetime import datetime, timezone

import bcrypt
from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel

from main import limiter

from .db import maps_collection, users_collection
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    build_owned_maps_sync_update,
    build_user_defaults_update,
    serialize_session_user,
)

router = APIRouter(prefix="/database/users")


class LoginPayload(BaseModel):
    username: str
    passwd: str

@router.post("/login")
@limiter.limit("2/minute")
def login_user(request: Request, username: str | None = None, passwd: str | None = None, body: LoginPayload | None = None):
    username = body.username if body else username
    passwd = body.passwd if body else passwd
    username = (username or "").strip()
    passwd = passwd or ""

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

    update: dict = {"$set": {"last_login_at": now}}
    rewarded = False

    if last_login_at is None or last_login_at.date() < now.date():
        update["$inc"] = {"maze_nuggets": DAILY_REWARD}
        rewarded = True

    set_updates = build_user_defaults_update(user)
    if set_updates:
        update.setdefault("$set", {}).update(set_updates)

    owned_to_add, owned_to_remove = build_owned_maps_sync_update(user, maps_collection)
    if owned_to_add:
        update.setdefault("$addToSet", {})["maps_owned"] = {"$each": owned_to_add}
    if owned_to_remove:
        update.setdefault("$pull", {})["maps_owned"] = {"$in": owned_to_remove}

    users_collection.update_one({"username": username}, update)

    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    return {
        "status": "success",
        "user": serialize_session_user(user, maps_collection),
        "daily_reward_granted": rewarded,
        "daily_reward_amount": DAILY_REWARD if rewarded else 0,
    }
