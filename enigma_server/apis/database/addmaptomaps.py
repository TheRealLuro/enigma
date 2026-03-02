from datetime import datetime, timezone

from fastapi import APIRouter, HTTPException, Request

from imagegen import generate_map_image_payload
from main import limiter

from .db import maps_collection, users_collection
from .imageupload import upload_image
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
):
    if maps_collection.find_one({"map_name": map_name}) or maps_collection.find_one({"seed": seed}):
        raise HTTPException(status_code=400, detail="Map already exists")

    try:
        hours, minutes, seconds, milliseconds = map(int, time_completed.split(":"))
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid time format. Use HH:MM:SS:MS")

    if first_rating is not None and (first_rating < 1 or first_rating > 10):
        raise HTTPException(status_code=400, detail="Rating must be 1-10")

    rating = [first_rating]
    value = int(round(map_audit(seed)))

    try:
        payload = generate_map_image_payload(seed)
    except RuntimeError as exc:
        raise HTTPException(status_code=503, detail=str(exc))

    map_theme = payload.get("theme")
    map_image = upload_image(payload["map_image"])
    image_status = "ready" if map_image else "pending_upload"
    image_upload_error = None if map_image else "Image upload deferred until the hosting service is available again."

    result = maps_collection.insert_one(
        {
            "map_name": map_name,
            "map_image": map_image,
            "image_status": image_status,
            "image_upload_error": image_upload_error,
            "theme": map_theme,
            "seed": seed,
            "value": value,
            "size": size,
            "difficulty": difficulty,
            "sold_for_last": 0,
            "owner": founder,
            "last_bought": None,
            "founder": founder,
            "time_founded": datetime.now(timezone.utc),
            "best_time": {
                "hours": hours,
                "minutes": minutes,
                "seconds": seconds,
                "milliseconds": milliseconds,
            },
            "user_with_best_time": founder,
            "rating": rating,
            "plays": 1,
        }
    )

    map_id = result.inserted_id

    update_result = users_collection.update_one(
        {"username": founder},
        {
            "$addToSet": {"maps_discovered": map_id, "maps_owned": map_id},
            "$inc": {"maze_nuggets": int(round(value))},
        },
    )

    return {
        "status": "success",
        "map_id": str(map_id),
        "image_status": image_status,
        "maps_updated": update_result.modified_count,
    }
