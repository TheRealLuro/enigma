from __future__ import annotations

from datetime import datetime, timedelta, timezone
from typing import Any, Iterable

from .map_utils import load_maps_by_ids, normalize_int, serialize_user_map_documents
from .redis_store import load_user_invites

SYSTEM_BANK_USERNAME = "enigma_bank"
TUTORIAL_VERSION = 1
USERNAME_CHANGE_COOLDOWN_DAYS = 14
ONLINE_STATUS_WINDOW_SECONDS = 120
PRESENCE_TOUCH_INTERVAL_SECONDS = 20
DEFAULT_PROFILE_IMAGE_CROP = {
    "x": 11.0,
    "y": 17.0,
    "size": 73.0,
}


def _safe_float(value: Any, default: float) -> float:
    try:
        parsed = float(value)
    except (TypeError, ValueError):
        return default

    if parsed != parsed:  # NaN guard
        return default

    return parsed


def normalize_email(value: str | None) -> str:
    return (value or "").strip().lower()


def _parse_datetime_utc(value: Any) -> datetime | None:
    if isinstance(value, datetime):
        parsed = value
    elif isinstance(value, str):
        trimmed = value.strip()
        if not trimmed:
            return None
        try:
            parsed = datetime.fromisoformat(trimmed.replace("Z", "+00:00"))
        except ValueError:
            return None
    else:
        return None

    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)

    return parsed.astimezone(timezone.utc)


def is_user_online(user: dict[str, Any], now: datetime | None = None) -> bool:
    last_seen_at = _parse_datetime_utc(user.get("last_seen_at"))
    if last_seen_at is None:
        return False

    current = now or datetime.now(timezone.utc)
    if current.tzinfo is None:
        current = current.replace(tzinfo=timezone.utc)

    return (current - last_seen_at) <= timedelta(seconds=ONLINE_STATUS_WINDOW_SECONDS)


def touch_user_presence(users_collection, username: str, min_interval_seconds: int = PRESENCE_TOUCH_INTERVAL_SECONDS) -> None:
    normalized_username = str(username or "").strip()
    if not normalized_username:
        return

    now = datetime.now(timezone.utc)
    min_interval = max(0, int(min_interval_seconds or 0))
    stale_before = now - timedelta(seconds=min_interval)

    users_collection.update_one(
        {
            "username": normalized_username,
            "$or": [
                {"last_seen_at": {"$exists": False}},
                {"last_seen_at": None},
                {"last_seen_at": {"$lt": stale_before}},
                {"last_seen_at": {"$type": "string"}},
            ],
        },
        {"$set": {"last_seen_at": now}},
    )


def default_tutorial_state() -> dict[str, Any]:
    return {
        "version": TUTORIAL_VERSION,
        "seen_at": None,
        "completed_at": None,
        "skipped_at": None,
    }


def default_user_fields(username: str | None = None, email: str | None = None) -> dict[str, Any]:
    is_system_account = (username or "").strip().lower() == SYSTEM_BANK_USERNAME
    return {
        "profile_image": None,
        "tutorial_state": default_tutorial_state(),
        "is_system_account": is_system_account,
        "allow_public_profile": not is_system_account,
        "email_normalized": normalize_email(email),
        "last_login_at": datetime.now(timezone.utc),
        "last_seen_at": None,
        "last_username_change_at": None,
        "last_staking_reward_at": None,
        "staked_map_ids": [],
        "staked_map_lock_until": {},
        "staked_map_started_at": {},
        "owned_cosmetics": [],
        "item_counts": {},
    }


def apply_user_defaults(user: dict[str, Any]) -> dict[str, Any]:
    merged = dict(user)
    defaults = default_user_fields(user.get("username"), user.get("email"))
    for key, value in defaults.items():
        merged.setdefault(key, value)

    if not merged.get("email_normalized"):
        merged["email_normalized"] = normalize_email(merged.get("email"))

    return merged


def build_user_defaults_update(user: dict[str, Any]) -> dict[str, Any]:
    updates: dict[str, Any] = {}
    defaults = default_user_fields(user.get("username"), user.get("email"))

    for key, value in defaults.items():
        if key not in user:
            updates[key] = value

    normalized_email = normalize_email(user.get("email"))
    if normalized_email and user.get("email_normalized") != normalized_email:
        updates["email_normalized"] = normalized_email

    username = (user.get("username") or "").strip().lower()
    is_system_account = username == SYSTEM_BANK_USERNAME
    if user.get("is_system_account") != is_system_account:
        updates["is_system_account"] = is_system_account
    if user.get("allow_public_profile") is None:
        updates["allow_public_profile"] = not is_system_account

    return updates


def build_owned_maps_sync_update(user: dict[str, Any], maps_collection) -> tuple[list[Any], list[Any]]:
    username = str(user.get("username") or "").strip()
    stored_owned_ids = list(user.get("maps_owned", []))
    stored_owned_id_strings = {str(map_id) for map_id in stored_owned_ids}

    actual_owned_docs = list(maps_collection.find({"owner": username}, {"_id": 1}))
    actual_owned_ids = [doc["_id"] for doc in actual_owned_docs]
    actual_owned_id_strings = {str(map_id) for map_id in actual_owned_ids}

    ids_to_add = [map_id for map_id in actual_owned_ids if str(map_id) not in stored_owned_id_strings]
    ids_to_remove = [map_id for map_id in stored_owned_ids if str(map_id) not in actual_owned_id_strings]
    return ids_to_add, ids_to_remove


def _merge_map_docs(primary: Iterable[dict[str, Any]], secondary: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    merged: list[dict[str, Any]] = []
    seen: set[str] = set()

    for document in [*primary, *secondary]:
        map_id = str(document.get("_id", ""))
        if not map_id or map_id in seen:
            continue

        seen.add(map_id)
        merged.append(document)

    return merged


def _count_owned_staked_maps(user: dict[str, Any], owned_map_docs: Iterable[dict[str, Any]]) -> int:
    staked_ids = {
        str(map_id).strip()
        for map_id in list(user.get("staked_map_ids", []) or [])
        if str(map_id).strip()
    }
    if not staked_ids:
        return 0

    owned_ids = {
        str(map_doc.get("_id") or "").strip()
        for map_doc in owned_map_docs
        if str(map_doc.get("_id") or "").strip()
    }
    if not owned_ids:
        return 0

    return len(staked_ids.intersection(owned_ids))


def resolve_user_maps(user: dict[str, Any], maps_collection) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    username = user.get("username", "")
    owned_map_docs = load_maps_by_ids(user.get("maps_owned", []), maps_collection)
    owner_docs = list(maps_collection.find({"owner": username}))
    discovered_map_docs = load_maps_by_ids(user.get("maps_discovered", []), maps_collection)
    return _merge_map_docs(owned_map_docs, owner_docs), _merge_map_docs(discovered_map_docs, [])


def serialize_profile_image(profile_image: Any, map_docs: Iterable[dict[str, Any]]) -> dict[str, Any] | None:
    if not isinstance(profile_image, dict):
        return None

    map_name = str(profile_image.get("map_name") or "").strip()
    stored_image_url = str(profile_image.get("image_url") or "").strip() or None
    image_url = stored_image_url
    crop = profile_image.get("crop") if isinstance(profile_image.get("crop"), dict) else {}

    if not map_name:
        return None

    for map_doc in map_docs:
        if str(map_doc.get("map_name") or "").strip().lower() == map_name.lower():
            image_url = map_doc.get("map_image") or stored_image_url
            break

    return {
        "map_name": map_name,
        "image_url": image_url,
        "crop": {
            "x": _safe_float(crop.get("x", DEFAULT_PROFILE_IMAGE_CROP["x"]), DEFAULT_PROFILE_IMAGE_CROP["x"]),
            "y": _safe_float(crop.get("y", DEFAULT_PROFILE_IMAGE_CROP["y"]), DEFAULT_PROFILE_IMAGE_CROP["y"]),
            "size": _safe_float(crop.get("size", DEFAULT_PROFILE_IMAGE_CROP["size"]), DEFAULT_PROFILE_IMAGE_CROP["size"]),
        },
        "updated_at": profile_image.get("updated_at"),
    }


def serialize_tutorial_state(value: Any) -> dict[str, Any]:
    source = value if isinstance(value, dict) else {}
    defaults = default_tutorial_state()
    return {
        "version": normalize_int(source.get("version"), defaults["version"]),
        "seen_at": source.get("seen_at"),
        "completed_at": source.get("completed_at"),
        "skipped_at": source.get("skipped_at"),
    }


def serialize_pending_multiplayer_invites(username: str | None) -> list[dict[str, Any]]:
    invites = load_user_invites(str(username or "").strip())
    serialized: list[dict[str, Any]] = []

    for session_id, payload in invites.items():
        if not isinstance(payload, dict):
            continue

        serialized.append(
            {
                "session_id": str(payload.get("session_id") or session_id),
                "owner_username": str(payload.get("owner_username") or "").strip(),
                "map_name": str(payload.get("map_name") or "").strip() or None,
                "difficulty": str(payload.get("difficulty") or "").strip(),
                "size": normalize_int(payload.get("size")),
                "status": str(payload.get("status") or "").strip(),
                "created_at": payload.get("created_at"),
                "source": str(payload.get("source") or "").strip() or "new",
            }
        )

    return sorted(
        serialized,
        key=lambda invite: (
            str(invite.get("created_at") or ""),
            str(invite.get("session_id") or ""),
        ),
        reverse=True,
    )


def serialize_session_user(user: dict[str, Any], maps_collection) -> dict[str, Any]:
    normalized_user = apply_user_defaults(user)
    maps_owned_docs, maps_discovered_docs = resolve_user_maps(normalized_user, maps_collection)
    staked_maps_count = _count_owned_staked_maps(normalized_user, maps_owned_docs)

    last_login_at = normalized_user.get("last_login_at")
    if isinstance(last_login_at, datetime):
        if last_login_at.tzinfo is None:
            last_login_at = last_login_at.replace(tzinfo=timezone.utc)
        last_login_value = last_login_at.astimezone(timezone.utc).isoformat()
    else:
        last_login_value = None

    last_username_change_at = normalized_user.get("last_username_change_at")
    if isinstance(last_username_change_at, datetime):
        if last_username_change_at.tzinfo is None:
            last_username_change_at = last_username_change_at.replace(tzinfo=timezone.utc)
        last_username_change_value = last_username_change_at.astimezone(timezone.utc).isoformat()
    else:
        last_username_change_value = str(last_username_change_at or "").strip() or None

    next_username_change_value = None
    if last_username_change_value:
        try:
            parsed_username_change = datetime.fromisoformat(last_username_change_value.replace("Z", "+00:00"))
            if parsed_username_change.tzinfo is None:
                parsed_username_change = parsed_username_change.replace(tzinfo=timezone.utc)
            next_username_change_value = (
                parsed_username_change.astimezone(timezone.utc)
                + timedelta(days=USERNAME_CHANGE_COOLDOWN_DAYS)
            ).isoformat()
        except ValueError:
            next_username_change_value = None

    return {
        "username": normalized_user.get("username", ""),
        "email": normalized_user.get("email", ""),
        "maze_nuggets": normalize_int(normalized_user.get("maze_nuggets")),
        "friends": list(normalized_user.get("friends", [])),
        "friend_requests": list(normalized_user.get("friend_requests", [])),
        "maps_owned": serialize_user_map_documents(maps_owned_docs),
        "maps_discovered": serialize_user_map_documents(maps_discovered_docs),
        "number_of_maps_played": normalize_int(normalized_user.get("number_of_maps_played")),
        "maps_completed": normalize_int(normalized_user.get("maps_completed")),
        "maps_lost": normalize_int(normalized_user.get("maps_lost")),
        "owned_cosmetics": list(normalized_user.get("owned_cosmetics", [])),
        "item_counts": normalized_user.get("item_counts", {}),
        "staked_maps_count": staked_maps_count,
        "last_staking_reward_at": normalized_user.get("last_staking_reward_at"),
        "pending_multiplayer_invites": serialize_pending_multiplayer_invites(normalized_user.get("username")),
        "last_login_at": last_login_value,
        "is_online": is_user_online(normalized_user),
        "last_username_change_at": last_username_change_value,
        "username_change_cooldown_days": USERNAME_CHANGE_COOLDOWN_DAYS,
        "next_username_change_at": next_username_change_value,
        "profile_image": serialize_profile_image(normalized_user.get("profile_image"), maps_owned_docs),
        "tutorial_state": serialize_tutorial_state(normalized_user.get("tutorial_state")),
        "is_system_account": bool(normalized_user.get("is_system_account")),
        "allow_public_profile": bool(normalized_user.get("allow_public_profile", True)),
        "owned_maps_count": len(serialize_user_map_documents(maps_owned_docs)),
        "discovered_maps_count": len(serialize_user_map_documents(maps_discovered_docs)),
    }


def serialize_public_profile(
    user: dict[str, Any],
    maps_collection,
    viewer_username: str | None = None,
) -> dict[str, Any]:
    normalized_user = apply_user_defaults(user)
    maps_owned_docs, maps_discovered_docs = resolve_user_maps(normalized_user, maps_collection)
    staked_maps_count = _count_owned_staked_maps(normalized_user, maps_owned_docs)
    is_self = (viewer_username or "").strip().lower() == str(normalized_user.get("username", "")).lower()
    friends = list(normalized_user.get("friends", [])) if is_self else []
    friend_requests = list(normalized_user.get("friend_requests", [])) if is_self else []

    last_username_change_at = normalized_user.get("last_username_change_at")
    if isinstance(last_username_change_at, datetime):
        if last_username_change_at.tzinfo is None:
            last_username_change_at = last_username_change_at.replace(tzinfo=timezone.utc)
        last_username_change_value = last_username_change_at.astimezone(timezone.utc).isoformat()
    else:
        last_username_change_value = str(last_username_change_at or "").strip() or None

    next_username_change_value = None
    if is_self and last_username_change_value:
        try:
            parsed_username_change = datetime.fromisoformat(last_username_change_value.replace("Z", "+00:00"))
            if parsed_username_change.tzinfo is None:
                parsed_username_change = parsed_username_change.replace(tzinfo=timezone.utc)
            next_username_change_value = (
                parsed_username_change.astimezone(timezone.utc)
                + timedelta(days=USERNAME_CHANGE_COOLDOWN_DAYS)
            ).isoformat()
        except ValueError:
            next_username_change_value = None

    return {
        "id": str(normalized_user.get("_id", "")),
        "username": normalized_user.get("username", ""),
        "maze_nuggets": normalize_int(normalized_user.get("maze_nuggets")),
        "friends": friends,
        "friend_requests": friend_requests,
        "number_of_maps_played": normalize_int(normalized_user.get("number_of_maps_played")),
        "maps_completed": normalize_int(normalized_user.get("maps_completed")),
        "maps_lost": normalize_int(normalized_user.get("maps_lost")),
        "maps_owned": serialize_user_map_documents(maps_owned_docs),
        "maps_discovered": serialize_user_map_documents(maps_discovered_docs),
        "owned_cosmetics": list(normalized_user.get("owned_cosmetics", [])) if is_self else [],
        "staked_maps_count": staked_maps_count,
        "last_staking_reward_at": normalized_user.get("last_staking_reward_at") if is_self else None,
        "owned_maps_count": len(serialize_user_map_documents(maps_owned_docs)),
        "discovered_maps_count": len(serialize_user_map_documents(maps_discovered_docs)),
        "is_online": is_user_online(normalized_user),
        "last_username_change_at": last_username_change_value if is_self else None,
        "username_change_cooldown_days": USERNAME_CHANGE_COOLDOWN_DAYS if is_self else 0,
        "next_username_change_at": next_username_change_value if is_self else None,
        "profile_image": serialize_profile_image(normalized_user.get("profile_image"), maps_owned_docs),
        "is_system_account": bool(normalized_user.get("is_system_account")),
        "allow_public_profile": bool(normalized_user.get("allow_public_profile", True)),
    }


def is_hidden_account(user: dict[str, Any] | None) -> bool:
    if not user:
        return True

    normalized_user = apply_user_defaults(user)
    return bool(normalized_user.get("is_system_account")) or not bool(normalized_user.get("allow_public_profile", True))
