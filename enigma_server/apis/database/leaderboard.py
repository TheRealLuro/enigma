from fastapi import APIRouter, HTTPException, Query
from .db import maps_collection, app_token

router = APIRouter(prefix="/database/leaderboard")

@router.get("/leaderboard")
def get_maps_leaderboard(
    token: str,
    sort_by: str = Query("rating"),
    order: str = Query("desc"),
):

    allowed_fields = [
        "rating",
        "plays",
        "best_time",
        "time_founded",
        "difficulty",
        "map_name"
    ]

    import hmac
    if not hmac.compare_digest(token, app_token):
        raise HTTPException(401)

    if sort_by not in allowed_fields:
        raise HTTPException(status_code=400, detail="Invalid sort field")

    sort_order = -1 if order.lower() == "desc" else 1

    pipeline = [
        {
            "$addFields": {
                "rating": {
                    "$ifNull": [{ "$avg": "$ratings" }, 0]
                }
            }
        },
        {
            "$sort": { sort_by: sort_order }
        }
    ]

    maps = list(maps_collection.aggregate(pipeline))

    for m in maps:
        m["_id"] = str(m["_id"])

    return {"maps": maps}
