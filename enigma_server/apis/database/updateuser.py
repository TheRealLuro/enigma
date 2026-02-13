from fastapi import APIRouter, HTTPException, Request
from .db import users_collection, maps_collection, app_token
from main import limiter
from decoder import decode

router = APIRouter(prefix="/database/users")


@router.put("/update_progress")
@limiter.limit("1/minute")
def update_user_progress(
    request: Request,
    username: str,
    map_seed: str,
    token: str,
    seed_existed: bool = True,
    map_lost: bool = False,
):

    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)


    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

   
    map_doc = maps_collection.find_one({"seed": map_seed})
    if not map_doc:
        raise HTTPException(status_code=404, detail="Map not found")
    map_id = map_doc["_id"]

    update_query = {"$inc": {"number_of_maps_played": 1}}

    
    if map_lost:
        update_query["$inc"]["maps_lost"] = 1
    else:
        update_query["$inc"]["maps_completed"] = 1

    
    if not seed_existed:
        update_query["$addToSet"] = {"maps_discovered": map_id}

    result = users_collection.update_one({"username": username}, update_query)

    return {"status": "success", "modified_count": result.modified_count}
