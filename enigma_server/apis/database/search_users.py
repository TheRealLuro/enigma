import re

from fastapi import APIRouter, Query, Request

from main import limiter

from .db import users_collection

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

    username_pattern = {"$regex": f"^{re.escape(normalized_query)}", "$options": "i"}
    cursor = (
        users_collection.find({"username": username_pattern}, {"username": 1, "maze_nuggets": 1})
        .sort("username", 1)
        .limit(limit)
    )

    return {
        "status": "success",
        "users": [
            {
                "username": user.get("username", ""),
                "maze_nuggets": int(user.get("maze_nuggets", 0) or 0),
            }
            for user in cursor
            if user.get("username")
        ],
    }
