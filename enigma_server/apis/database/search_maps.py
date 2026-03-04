import re
from typing import Any

from fastapi import APIRouter, Query, Request

from main import limiter

from .db import maps_collection
from .map_utils import serialize_map_documents

router = APIRouter(prefix="/database/maps")


@router.get("/search")
@limiter.limit("30/minute")
def search_maps(
    request: Request,
    query: str = Query(..., min_length=1),
    limit: int = Query(6, ge=1, le=12),
):
    normalized_query = query.strip()
    if not normalized_query:
        return {"status": "success", "maps": []}

    contains_pattern = re.compile(re.escape(normalized_query), re.IGNORECASE)
    starts_pattern = re.compile(f"^{re.escape(normalized_query)}", re.IGNORECASE)
    docs = list(
        maps_collection.find(
            {"map_name": contains_pattern},
            {
                "map_name": 1,
                "map_image": 1,
                "image_status": 1,
                "image_upload_error": 1,
                "theme": 1,
                "difficulty": 1,
                "size": 1,
                "founder": 1,
                "owner": 1,
                "value": 1,
                "sold_for_last": 1,
                "plays": 1,
                "best_time": 1,
                "user_with_best_time": 1,
                "time_founded": 1,
                "rating": 1,
            },
        ).limit(limit * 3)
    )

    def sort_key(map_doc: dict):
        map_name = str(map_doc.get("map_name") or "")
        normalized_map_name = map_name.lower()
        normalized_search = normalized_query.lower()
        exact_rank = 0 if normalized_map_name == normalized_search else 1
        prefix_rank = 0 if starts_pattern.search(map_name) else 1
        contains_rank = 0 if contains_pattern.search(map_name) else 1
        return (exact_rank, prefix_rank, contains_rank, normalized_map_name)

    docs.sort(key=sort_key)

    return {
        "status": "success",
        "maps": serialize_map_documents(docs[:limit]),
    }
