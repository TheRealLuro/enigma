from fastapi import APIRouter, HTTPException
from .db import users_collection, maps_collection
from bson import ObjectId

router = APIRouter(prefix="/database/users")
## only call after game is won or lost.

@router.put("/update_progress")
def update_user_progress(
    username: str,
    map_seed: str,
    seed_existed: bool = True,
    map_lost: bool = False
):
    # Find user
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    # Find map ObjectId
    map_doc = maps_collection.find_one({"seed": map_seed})
    if not map_doc:
        raise HTTPException(status_code=404, detail="Map not found")
    map_id = map_doc["_id"]

    update_query = {"$inc": {"number_of_maps_played": 1}}

    # Only increment maps_lost if map_lost = True
    if map_lost:
        update_query["$inc"]["maps_lost"] = 1
    else:
        update_query["$inc"]["maps_completed"] = 1

    # Only add to maps_discovered if seed did not exist
    if not seed_existed:
        update_query["$addToSet"] = {"maps_discovered": map_id}

    result = users_collection.update_one({"username": username}, update_query)

    return {"status": "success", "modified_count": result.modified_count}
