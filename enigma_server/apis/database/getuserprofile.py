from fastapi import APIRouter, HTTPException, Request
from .db import users_collection, maps_collection
from main import limiter
from bson import ObjectId

router = APIRouter(prefix="/database/users")

@router.get("/getuser")
@limiter.limit("10/minute")
def get_user(request: Request, username: str):
    
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")


    user["_id"] = str(user["_id"])


    user_maps = user.get("maps_discovered", [])

    maps = []
    for map in user_maps:
        map_doc = maps_collection.find_one({"_id": ObjectId(map)})
        if map_doc:
            map_doc.pop("_id", None)
            maps.append(map_doc)

    user["maps_discovered"] = maps

    user_data = user.copy()
    user_data.pop("email", None)
    user_data.pop("friends", None)
    user_data.pop("friend_requests", None)
    user_data.pop("owned_cosmetics", None)
    user_data.pop("item_counts", None)
    user_data.pop("last_login_at", None)
    user_data.pop("password", None)




    return {"status": "success", "user": user_data}
