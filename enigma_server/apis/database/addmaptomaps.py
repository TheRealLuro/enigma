from fastapi import APIRouter, HTTPException
from .db import maps_collection, users_collection, app_token
from datetime import datetime
from bson import ObjectId
from slowapi import limiter


router = APIRouter(prefix="/database/maps")


@router.post("/add")
@limiter.limit("1/minute")
def add_map(
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
    if not hmac.compare_digest(token, app_token):
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

    result = maps_collection.insert_one({
        "map_name": map_name,
        "seed": seed,
        "size": size,
        "difficulty": difficulty,
        "founder": founder, 
        "time_founded": datetime.utcnow(),
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
        {"$addToSet": {"maps_discovered": map_id}}
    )

    return {
        "status": "success",
        "map_id": str(map_id),
        "maps_discovered_updated": update_result.modified_count
    }
