from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Iterable

from .map_utils import load_maps_by_ids, normalize_int, serialize_user_map_documents

SYSTEM_BANK_USERNAME = "enigma_bank"
TUTORIAL_VERSION = 1


def normalize_email(value: str | None) -> str:
    return (value or "").strip().lower()


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


def build_discovered_to_owned_sync_update(user: dict[str, Any]) -> list[Any]:
    discovered_ids = list(user.get("maps_discovered", []))
    owned_id_strings = {str(map_id) for map_id in user.get("maps_owned", [])}
    return [map_id for map_id in discovered_ids if str(map_id) not in owned_id_strings]


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
    image_url = str(profile_image.get("image_url") or "").strip() or None
    crop = profile_image.get("crop") if isinstance(profile_image.get("crop"), dict) else {}

    if not map_name:
        return None

    if not image_url:
        for map_doc in map_docs:
            if str(map_doc.get("map_name") or "").strip().lower() == map_name.lower():
                image_url = map_doc.get("map_image") or None
                break

    return {
        "map_name": map_name,
        "image_url": image_url,
        "crop": {
            "x": float(crop.get("x", 0)),
            "y": float(crop.get("y", 0)),
            "size": float(crop.get("size", 100)),
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


def serialize_session_user(user: dict[str, Any], maps_collection) -> dict[str, Any]:
    normalized_user = apply_user_defaults(user)
    maps_owned_docs, maps_discovered_docs = resolve_user_maps(normalized_user, maps_collection)

    last_login_at = normalized_user.get("last_login_at")
    if isinstance(last_login_at, datetime):
        if last_login_at.tzinfo is None:
            last_login_at = last_login_at.replace(tzinfo=timezone.utc)
        last_login_value = last_login_at.astimezone(timezone.utc).isoformat()
    else:
        last_login_value = None

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
        "last_login_at": last_login_value,
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
    is_self = (viewer_username or "").strip().lower() == str(normalized_user.get("username", "")).lower()
    friends = list(normalized_user.get("friends", [])) if is_self else []
    friend_requests = list(normalized_user.get("friend_requests", [])) if is_self else []

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
        "owned_maps_count": len(serialize_user_map_documents(maps_owned_docs)),
        "discovered_maps_count": len(serialize_user_map_documents(maps_discovered_docs)),
        "profile_image": serialize_profile_image(normalized_user.get("profile_image"), maps_owned_docs),
        "is_system_account": bool(normalized_user.get("is_system_account")),
        "allow_public_profile": bool(normalized_user.get("allow_public_profile", True)),
    }


def is_hidden_account(user: dict[str, Any] | None) -> bool:
    if not user:
        return True

    normalized_user = apply_user_defaults(user)
    return bool(normalized_user.get("is_system_account")) or not bool(normalized_user.get("allow_public_profile", True))
