from fastapi import APIRouter, HTTPException
from .db import maps_collection, users_collection
from datetime import datetime
from bson import ObjectId

router = APIRouter(prefix="/database/maps")

## only call when its a new seed

@router.post("/add")
def add_map(
    map_name: str,
    seed: str,
    size: int,
    difficulty: str,
    founder: str,
    time_completed: str,
    first_rating: int
):
    # Check if map already exists by name or seed
    if maps_collection.find_one({"map_name": map_name}) or maps_collection.find_one({"seed": seed}):
        raise HTTPException(status_code=400, detail="Map already exists")

    # Parse completion time
    try:
        hours, minutes, seconds, milliseconds = map(int, time_completed.split(":"))
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid time format. Use HH:MM:SS:MS")

    # Validate rating
    if first_rating is not None:
        if first_rating < 1 or first_rating > 10:
            raise HTTPException(status_code=400, detail="Rating must be 1-10")

    rating = [first_rating]

    # Insert map
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

    # Add map ObjectId to founder's maps_discovered
    update_result = users_collection.update_one(
        {"username": founder},
        {"$addToSet": {"maps_discovered": map_id}}
    )

    return {
        "status": "success",
        "map_id": str(map_id),
        "maps_discovered_updated": update_result.modified_count
    }
