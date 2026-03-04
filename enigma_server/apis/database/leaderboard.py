from fastapi import APIRouter, HTTPException, Query

from .db import maps_collection
from .map_utils import serialize_map_document

router = APIRouter(prefix="/database/leaderboard")

DIFFICULTY_ORDER = {
    "easy": 1,
    "medium": 2,
    "hard": 3,
}


@router.get("/leaderboard")
def get_maps_leaderboard(
    sort_by: str = Query("rating"),
    order: str = Query("desc"),
    limit: int = Query(10, ge=1, le=100),
    offset: int = Query(0, ge=0),
):
    allowed_fields = [
        "rating",
        "plays",
        "best_time",
        "time_founded",
        "difficulty",
        "map_name",
    ]

    if sort_by not in allowed_fields:
        raise HTTPException(status_code=400, detail="Invalid sort field")

    normalized_order = order.lower()
    if normalized_order not in {"asc", "desc"}:
        raise HTTPException(status_code=400, detail="Invalid sort order")

    reverse = normalized_order == "desc"
    serialized_maps = [serialize_map_document(map_doc) for map_doc in maps_collection.find({})]

    def sort_key(map_doc: dict):
        if sort_by == "rating":
            return map_doc.get("rating_average", 0)
        if sort_by == "plays":
            return map_doc.get("plays", 0)
        if sort_by == "best_time":
            best_time_ms = map_doc.get("best_time_ms")
            return best_time_ms if best_time_ms is not None else (10**15 if not reverse else -1)
        if sort_by == "time_founded":
            return map_doc.get("time_founded") or ""
        if sort_by == "difficulty":
            return DIFFICULTY_ORDER.get((map_doc.get("difficulty") or "").lower(), 0)
        return (map_doc.get("map_name") or "").lower()

    serialized_maps.sort(key=sort_key, reverse=reverse)
    total_count = len(serialized_maps)
    page_maps = serialized_maps[offset : offset + limit]

    return {
        "status": "success",
        "sort_by": sort_by,
        "order": normalized_order,
        "limit": limit,
        "offset": offset,
        "total_count": total_count,
        "maps": page_maps,
    }
