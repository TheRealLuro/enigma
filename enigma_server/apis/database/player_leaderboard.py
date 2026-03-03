from fastapi import APIRouter, HTTPException, Query

from .db import users_collection
from .map_utils import normalize_int
from .user_utils import apply_user_defaults

router = APIRouter(prefix="/database/leaderboard")


@router.get("/players")
def get_players_leaderboard(
    sort_by: str = Query("maze_nuggets"),
    order: str = Query("desc"),
):
    allowed_fields = [
        "maze_nuggets",
        "owned_maps",
        "discovered_maps",
        "wins",
        "losses",
        "win_rate",
        "maps_played",
        "username",
    ]

    if sort_by not in allowed_fields:
        raise HTTPException(status_code=400, detail="Invalid sort field")

    normalized_order = order.lower()
    if normalized_order not in {"asc", "desc"}:
        raise HTTPException(status_code=400, detail="Invalid sort order")

    reverse = normalized_order == "desc"
    leaderboard: list[dict] = []

    for user in users_collection.find({}, {
        "username": 1,
        "maze_nuggets": 1,
        "maps_owned": 1,
        "maps_discovered": 1,
        "maps_completed": 1,
        "maps_lost": 1,
        "number_of_maps_played": 1,
        "is_system_account": 1,
        "allow_public_profile": 1,
        "profile_image": 1,
    }):
        user = apply_user_defaults(user)
        if user.get("is_system_account") or not user.get("allow_public_profile", True):
            continue

        owned_ids = {str(map_id) for map_id in user.get("maps_owned", [])}
        discovered_ids = {str(map_id) for map_id in user.get("maps_discovered", [])}
        effective_owned_count = len(owned_ids | discovered_ids)
        discovered_count = len(discovered_ids)
        wins = normalize_int(user.get("maps_completed"))
        losses = normalize_int(user.get("maps_lost"))
        maps_played = normalize_int(user.get("number_of_maps_played"))
        win_rate = round((wins / maps_played) * 100, 1) if maps_played > 0 else 0.0

        leaderboard.append(
            {
                "username": user.get("username", ""),
                "maze_nuggets": normalize_int(user.get("maze_nuggets")),
                "owned_maps_count": effective_owned_count,
                "discovered_maps_count": discovered_count,
                "maps_completed": wins,
                "maps_lost": losses,
                "maps_played": maps_played,
                "win_rate": win_rate,
                "profile_image": user.get("profile_image"),
            }
        )

    def sort_key(entry: dict):
        if sort_by == "maze_nuggets":
            return entry.get("maze_nuggets", 0)
        if sort_by == "owned_maps":
            return entry.get("owned_maps_count", 0)
        if sort_by == "discovered_maps":
            return entry.get("discovered_maps_count", 0)
        if sort_by == "wins":
            return entry.get("maps_completed", 0)
        if sort_by == "losses":
            return entry.get("maps_lost", 0)
        if sort_by == "win_rate":
            return entry.get("win_rate", 0.0)
        if sort_by == "maps_played":
            return entry.get("maps_played", 0)
        return (entry.get("username") or "").lower()

    leaderboard.sort(
        key=lambda entry: (sort_key(entry), (entry.get("username") or "").lower()),
        reverse=reverse,
    )

    return {
        "status": "success",
        "sort_by": sort_by,
        "order": normalized_order,
        "players": leaderboard,
    }
