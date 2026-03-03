from __future__ import annotations

from typing import Any


FOUNDERS_MARK_ITEM_ID = "founders_mark"
FOUNDERS_MARK_REWARD_MULTIPLIER = 1.5
FOUNDERS_MARK_FEE_REDUCTION = 0.5


def has_founders_mark(user: dict[str, Any] | None) -> bool:
    if not user:
        return False

    item_counts = user.get("item_counts", {})
    try:
        return int(item_counts.get(FOUNDERS_MARK_ITEM_ID, 0) or 0) > 0
    except (TypeError, ValueError):
        return False


def _qualifying_beta_maps(user: dict[str, Any], maps_collection) -> list[dict[str, Any]]:
    username = str(user.get("username") or "").strip()
    if not username:
        return []

    return list(
        maps_collection.find(
            {
                "founder": username,
                "size": 4,
                "difficulty": {"$regex": "^easy$", "$options": "i"},
            },
            {"map_name": 1, "map_image": 1},
        )
    )


def evaluate_founders_mark_requirements(user: dict[str, Any], maps_collection) -> dict[str, Any]:
    qualifying_maps = _qualifying_beta_maps(user, maps_collection)
    qualifying_map_names = {
        str(document.get("map_name") or "").strip()
        for document in qualifying_maps
        if str(document.get("map_name") or "").strip()
    }

    profile_image = user.get("profile_image") if isinstance(user.get("profile_image"), dict) else {}
    avatar_map_name = str(profile_image.get("map_name") or "").strip()
    friends = list(user.get("friends", []))

    requirements = [
        {
            "key": "saved_easy_4x4",
            "label": "Finish and save at least one 4x4 easy maze you founded.",
            "completed": bool(qualifying_map_names),
        },
        {
            "key": "avatar_set",
            "label": "Set your profile picture to that saved 4x4 easy maze.",
            "completed": bool(avatar_map_name and avatar_map_name in qualifying_map_names),
        },
        {
            "key": "friend_added",
            "label": "Add at least one friend to your network.",
            "completed": len([friend for friend in friends if str(friend).strip()]) > 0,
        },
    ]

    is_owned = has_founders_mark(user)
    eligible = all(bool(requirement["completed"]) for requirement in requirements) and not is_owned

    if is_owned:
        reason = "Already claimed."
    elif not qualifying_map_names:
        reason = "Finish and save a 4x4 easy maze you founded."
    elif avatar_map_name not in qualifying_map_names:
        reason = "Set your profile picture to your saved 4x4 easy maze."
    elif len([friend for friend in friends if str(friend).strip()]) == 0:
        reason = "Add one friend to unlock this founder item."
    else:
        reason = None

    return {
        "eligible": eligible,
        "is_owned": is_owned,
        "reason": reason,
        "requirements": requirements,
        "qualifying_map_names": sorted(qualifying_map_names, key=str.casefold),
    }
