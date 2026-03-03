from fastapi import APIRouter, HTTPException, Request

from main import limiter

from .db import maps_collection

router = APIRouter(prefix="/database/maps")


# send the seed, the player's username, their completion time as HH:MM:SS:MMM, and optionally send their rating.
def time_obj_to_ms(time_obj):
    return (
        time_obj["hours"] * 3600000
        + time_obj["minutes"] * 60000
        + time_obj["seconds"] * 1000
        + time_obj["milliseconds"]
    )


@router.put("/update_map")
@limiter.limit("1/minute")
def update_map(
    request: Request,
    seed: str,
    username: str,
    completion_time: str,
    rating: int | None = None,
):
    try:
        hours, minutes, seconds, milliseconds = map(int, completion_time.split(":"))
    except ValueError as exc:
        raise HTTPException(status_code=400, detail="Invalid completion time format") from exc

    time_to_compare = {
        "hours": hours,
        "minutes": minutes,
        "seconds": seconds,
        "milliseconds": milliseconds,
    }

    map_data = maps_collection.find_one({"seed": seed})
    if not map_data:
        raise HTTPException(status_code=404, detail="Map not found")

    update_query = {
        "$inc": {"plays": 1}
    }

    if rating is not None:
        if rating < 1 or rating > 10:
            raise HTTPException(status_code=400, detail="Rating must be 1-10")

        update_query.setdefault("$push", {})
        update_query["$push"]["rating"] = rating

    current_best_time = map_data.get("best_time") or {
        "hours": 99,
        "minutes": 59,
        "seconds": 59,
        "milliseconds": 999,
    }

    new_time_ms = time_obj_to_ms(time_to_compare)
    current_time_ms = time_obj_to_ms(current_best_time)

    if new_time_ms < current_time_ms:
        update_query.setdefault("$set", {})
        update_query["$set"]["best_time"] = time_to_compare
        update_query["$set"]["user_with_best_time"] = username

    maps_collection.update_one({"seed": seed}, update_query)

    return {"status": "success"}
