from fastapi import APIRouter, HTTPException, Request

from main import limiter

from .db import maps_collection, users_collection
from .map_utils import load_maps_by_ids, normalize_int, serialize_user_map_documents

router = APIRouter(prefix="/database/users")


@router.get("/getuser")
@limiter.limit("10/minute")
def get_user(request: Request, username: str):
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    discovered_ids = list(user.get("maps_discovered", []))
    owned_id_strings = {str(map_id) for map_id in user.get("maps_owned", [])}
    missing_owned_maps = [map_id for map_id in discovered_ids if str(map_id) not in owned_id_strings]
    if missing_owned_maps:
        users_collection.update_one(
            {"_id": user["_id"]},
            {"$addToSet": {"maps_owned": {"$each": missing_owned_maps}}},
        )
        user = users_collection.find_one({"_id": user["_id"]}) or user

    owned_map_docs = load_maps_by_ids(user.get("maps_owned", []), maps_collection)
    owner_docs = list(maps_collection.find({"owner": username}))
    discovered_map_docs = load_maps_by_ids(user.get("maps_discovered", []), maps_collection)

    maps_owned = serialize_user_map_documents([*owned_map_docs, *owner_docs])
    maps_discovered = serialize_user_map_documents(discovered_map_docs)

    user_data = {
        "id": str(user["_id"]),
        "username": user.get("username", ""),
        "maze_nuggets": normalize_int(user.get("maze_nuggets")),
        "friends": list(user.get("friends", [])),
        "friend_requests": list(user.get("friend_requests", [])),
        "number_of_maps_played": normalize_int(user.get("number_of_maps_played")),
        "maps_completed": normalize_int(user.get("maps_completed")),
        "maps_lost": normalize_int(user.get("maps_lost")),
        "maps_owned": maps_owned,
        "maps_discovered": maps_discovered,
        "owned_maps_count": len(maps_owned),
        "discovered_maps_count": len(maps_discovered),
    }

    return {"status": "success", "user": user_data}
