import re

from fastapi import APIRouter, Query, Request

from main import limiter

from .db import users_collection
from .user_utils import SYSTEM_BANK_USERNAME

router = APIRouter(prefix="/database/users")


@router.get("/search")
@limiter.limit("30/minute")
def search_users(
    request: Request,
    query: str = Query(..., min_length=1),
    limit: int = Query(6, ge=1, le=12),
):
    normalized_query = query.strip()
    if not normalized_query:
        return {"status": "success", "users": []}

    contains_pattern = re.compile(re.escape(normalized_query), re.IGNORECASE)
    starts_pattern = re.compile(f"^{re.escape(normalized_query)}", re.IGNORECASE)
    docs = list(
        users_collection.find(
            {
                "username": contains_pattern,
                "is_system_account": {"$ne": True},
                "allow_public_profile": {"$ne": False},
            },
            {
                "username": 1,
                "maze_nuggets": 1,
                "maps_completed": 1,
                "maps_lost": 1,
                "maps_owned": 1,
                "maps_discovered": 1,
                "profile_image": 1,
            },
        ).limit(limit * 3)
    )

    def sort_key(user: dict):
        username = user.get("username", "")
        normalized_username = username.lower()
        normalized_search = normalized_query.lower()
        exact_rank = 0 if normalized_username == normalized_search else 1
        prefix_rank = 0 if starts_pattern.search(username) else 1
        contains_rank = 0 if contains_pattern.search(username) else 1
        return (exact_rank, prefix_rank, contains_rank, normalized_username)

    docs.sort(key=sort_key)

    return {
        "status": "success",
        "users": [
            {
                "username": user.get("username", ""),
                "maze_nuggets": int(user.get("maze_nuggets", 0) or 0),
                "owned_maps_count": len({str(map_id) for map_id in user.get("maps_owned", [])}),
                "discovered_maps_count": len({str(map_id) for map_id in user.get("maps_discovered", [])}),
                "maps_completed": int(user.get("maps_completed", 0) or 0),
                "maps_lost": int(user.get("maps_lost", 0) or 0),
                "profile_image": user.get("profile_image"),
            }
            for user in docs[:limit]
            if user.get("username") and user.get("username") != SYSTEM_BANK_USERNAME
        ],
    }
