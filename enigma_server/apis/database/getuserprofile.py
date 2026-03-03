from fastapi import APIRouter, HTTPException, Request

from main import limiter

from .db import maps_collection, users_collection
from .user_utils import (
    build_discovered_to_owned_sync_update,
    build_user_defaults_update,
    is_hidden_account,
    serialize_public_profile,
)

router = APIRouter(prefix="/database/users")


@router.get("/getuser")
@limiter.limit("10/minute")
def get_user(request: Request, username: str, viewer: str | None = None):
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    if is_hidden_account(user) and (viewer or "").strip().lower() != username.strip().lower():
        raise HTTPException(status_code=404, detail="User not found")

    update_query: dict = {}
    set_updates = build_user_defaults_update(user)
    if set_updates:
        update_query["$set"] = set_updates

    missing_owned_maps = build_discovered_to_owned_sync_update(user)
    if missing_owned_maps:
        update_query["$addToSet"] = {"maps_owned": {"$each": missing_owned_maps}}

    if update_query:
        users_collection.update_one({"_id": user["_id"]}, update_query)
        user = users_collection.find_one({"_id": user["_id"]}) or user

    viewer_user = users_collection.find_one({"username": viewer}) if viewer else None
    user_data = serialize_public_profile(user, maps_collection, viewer_username=viewer)
    user_data["relationship"] = {
        "are_friends": bool(viewer_user and username in viewer_user.get("friends", [])),
        "incoming_request": bool(viewer_user and username in viewer_user.get("friend_requests", [])),
        "outgoing_request": bool(viewer and viewer in user.get("friend_requests", [])),
    }
    return {"status": "success", "user": user_data}
