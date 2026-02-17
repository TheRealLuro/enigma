from fastapi import APIRouter, HTTPException, Request
from .db import maps_collection, users_collection, app_token
from datetime import datetime
from main import limiter
from decoder import decode
from .map_audit import map_audit


router = APIRouter(prefix="/database/maps")


@router.post("/add")
@limiter.limit("1/minute")
def add_map(
    request: Request,
    map_name: str,
    seed: str,
    size: int,
    difficulty: str,
    founder: str,
    time_completed: str,
    first_rating: int,
    token: str,
):
    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)

    
    if maps_collection.find_one({"map_name": map_name}) or maps_collection.find_one({"seed": seed}):
        raise HTTPException(status_code=400, detail="Map already exists")


    try:
        hours, minutes, seconds, milliseconds = map(int, time_completed.split(":"))
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid time format. Use HH:MM:SS:MS")


    if first_rating is not None:
        if first_rating < 1 or first_rating > 10:
            raise HTTPException(status_code=400, detail="Rating must be 1-10")

    rating = [first_rating]
    value = map_audit(seed)

    result = maps_collection.insert_one({
        "map_name": map_name,
        "seed": seed,
        "value":  value,
        "size": size,
        "difficulty": difficulty,
        "sold_for_last": 0,
        "owner": founder,
        "last_bought": None,
        "founder": founder, 
        "time_founded": datetime.now(datetime.timezone.utc),
        "best_time": {
            "hours": hours,
            "minutes": minutes,
            "seconds": seconds,
            "milliseconds": milliseconds
        },
        "user_with_best_time": founder,
        "rating": rating,
        "plays": 1
    })

    map_id = result.inserted_id

    update_result = users_collection.update_one(
        {"username": founder},
        {"$addToSet": {"maps_discovered": map_id}},
        {"$inc": {"maze_nuggets": value}}
    )

    return {
        "status": "success",
        "map_id": str(map_id),
        "maps_discovered_updated": update_result.modified_count
    }
