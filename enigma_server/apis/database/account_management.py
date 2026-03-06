from __future__ import annotations

import re
import threading
import time
from datetime import datetime, timedelta, timezone
from typing import Any

import bcrypt
from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, Field
from pymongo.errors import DuplicateKeyError

from main import limiter

from .account_cleanup import delete_user_account
from .db import client, item_inventory, maps_collection, marketplace_collection, run_results, users_collection
from .economy_rules import compute_loss_fee, credit_bank_dividend
from .input_validation import (
    ensure_safe_text,
    validate_email_address,
    validate_login_username,
    validate_password_strength,
    validate_username,
)
from .item_catalog import is_item_supported_for_current_app, serialize_shop_item
from .redis_store import delete_keys, load_json, save_json, session_key, user_invites_key, user_session_key
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    USERNAME_CHANGE_COOLDOWN_DAYS,
    build_owned_maps_sync_update,
    build_user_defaults_update,
    normalize_email,
    resolve_user_maps,
    serialize_session_user,
    touch_user_presence,
)

router = APIRouter(prefix="/database/users")
ACCOUNT_LITE_CACHE_TTL_SECONDS = 0.75
MAX_ACCOUNT_LITE_CACHE_ENTRIES = 5000
_account_lite_cache_lock = threading.Lock()
_account_lite_cache: dict[str, tuple[float, dict[str, Any]]] = {}

DEFAULT_AVATAR_CROP = {
    "x": 11.0,
    "y": 17.0,
    "size": 73.0,
}


class UpdateEmailPayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    current_password: str = Field(min_length=1, max_length=128)
    new_email: str = Field(min_length=3, max_length=254)


class UpdatePasswordPayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    current_password: str = Field(min_length=1, max_length=128)
    new_password: str = Field(min_length=10, max_length=128)


class UpdateUsernamePayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    current_password: str = Field(min_length=1, max_length=128)
    new_username: str = Field(min_length=3, max_length=32)


class UpdateAvatarPayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    map_name: str = Field(min_length=1, max_length=120)


class DeleteAccountPayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    current_password: str = Field(min_length=1, max_length=128)
    confirm_username: str = Field(min_length=1, max_length=64)


class TutorialStatePayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    action: str = Field(min_length=1, max_length=32)


class RemoveFriendPayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    friend_username: str = Field(min_length=1, max_length=64)


class AbandonRunPayload(BaseModel):
    username: str = Field(min_length=1, max_length=64)
    run_nonce: str = Field(min_length=1, max_length=256)
    seed: str = Field(min_length=1, max_length=2048)
    used_items: list[str] = []
    map_name: str | None = Field(default=None, max_length=120)
    source: str = Field(default="new", max_length=32)
    forfeited_run_payout: int = 0
    projected_completion_payout: int = 0
    map_value: int = 0
    reason: str = Field(default="abandoned", max_length=64)


def _verify_password(user: dict[str, Any], password: str) -> None:
    password = ensure_safe_text(
        password,
        field_name="password",
        min_length=1,
        max_length=128,
    )
    hashed = user.get("password")
    if not hashed:
        raise HTTPException(status_code=400, detail="Password is not configured")

    hashed_bytes = hashed if isinstance(hashed, bytes) else str(hashed).encode("utf-8")
    if not bcrypt.checkpw(password.encode("utf-8"), hashed_bytes):
        raise HTTPException(status_code=401, detail="Incorrect password")


def _parse_datetime(value: Any) -> datetime | None:
    if isinstance(value, datetime):
        return value if value.tzinfo is not None else value.replace(tzinfo=timezone.utc)

    text = str(value or "").strip()
    if not text:
        return None

    try:
        parsed = datetime.fromisoformat(text.replace("Z", "+00:00"))
        return parsed if parsed.tzinfo is not None else parsed.replace(tzinfo=timezone.utc)
    except ValueError:
        return None


def _assert_username_cooldown_elapsed(user: dict[str, Any]) -> None:
    last_changed_at = _parse_datetime(user.get("last_username_change_at"))
    if last_changed_at is None:
        return

    now = datetime.now(timezone.utc)
    next_change_at = last_changed_at + timedelta(days=USERNAME_CHANGE_COOLDOWN_DAYS)
    if now >= next_change_at:
        return

    wait_seconds = int((next_change_at - now).total_seconds())
    wait_days = max(1, int((wait_seconds + 86399) // 86400))
    next_change_label = next_change_at.astimezone(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    raise HTTPException(
        status_code=429,
        detail=f"Username can be changed every {USERNAME_CHANGE_COOLDOWN_DAYS} days. "
        f"Try again in about {wait_days} day(s) (next available: {next_change_label}).",
    )


def _replace_username_values(values: list[Any], current_username: str, new_username: str) -> list[str]:
    normalized_current = current_username.strip().casefold()
    normalized_new = new_username.strip()
    dedupe: set[str] = set()
    replaced: list[str] = []

    for value in values:
        username = str(value or "").strip()
        if not username:
            continue

        if username.casefold() == normalized_current:
            username = normalized_new

        key = username.casefold()
        if key in dedupe:
            continue

        dedupe.add(key)
        replaced.append(username)

    return replaced


def _assert_username_change_multiplayer_safe(username: str) -> None:
    try:
        active_session_payload = load_json(user_session_key(username))
    except HTTPException:
        return

    active_session_id = ""
    if isinstance(active_session_payload, dict):
        active_session_id = str(active_session_payload.get("session_id") or "").strip()
    elif isinstance(active_session_payload, str):
        active_session_id = active_session_payload.strip()

    if not active_session_id:
        return

    try:
        active_session = load_json(session_key(active_session_id))
    except HTTPException:
        return

    active_status = str((active_session or {}).get("status") or "").strip().lower()
    if active_session and active_status not in {"completed", "abandoned"}:
        raise HTTPException(status_code=409, detail="Leave your active co-op session before changing username")


def _migrate_multiplayer_user_keys(current_username: str, new_username: str) -> None:
    try:
        current_invites = load_json(user_invites_key(current_username))
        new_invites = load_json(user_invites_key(new_username))
    except HTTPException:
        return

    merged_invites: dict[str, Any] = {}
    if isinstance(new_invites, dict):
        merged_invites.update(new_invites)
    if isinstance(current_invites, dict):
        merged_invites.update(current_invites)

    normalized_current = current_username.strip().casefold()

    def replace_value(value: Any) -> str:
        username = str(value or "").strip()
        if username.casefold() == normalized_current:
            return new_username
        return username

    def replace_list(values: Any) -> list[str]:
        return _replace_username_values(list(values or []), current_username, new_username)

    def replace_session_payload(session_payload: dict[str, Any]) -> bool:
        changed = False

        owner_username = replace_value(session_payload.get("owner_username"))
        if owner_username != str(session_payload.get("owner_username") or "").strip():
            session_payload["owner_username"] = owner_username
            changed = True

        guest_username = replace_value(session_payload.get("guest_username"))
        if guest_username != str(session_payload.get("guest_username") or "").strip():
            session_payload["guest_username"] = guest_username
            changed = True

        current_invited = list(session_payload.get("invited_friends", []))
        updated_invited = replace_list(current_invited)
        if updated_invited != current_invited:
            session_payload["invited_friends"] = updated_invited
            changed = True

        players = session_payload.get("players", {})
        if isinstance(players, dict):
            updated_players: dict[str, Any] = {}
            for player_username, player_state in players.items():
                normalized_username = replace_value(player_username)
                next_state = dict(player_state or {})
                if str(next_state.get("username") or "").strip().casefold() == normalized_current:
                    next_state["username"] = new_username
                    changed = True
                updated_players[normalized_username] = next_state
            if updated_players != players:
                session_payload["players"] = updated_players
                changed = True

        abandon_by = replace_value(session_payload.get("abandoned_by"))
        if abandon_by != str(session_payload.get("abandoned_by") or "").strip():
            session_payload["abandoned_by"] = abandon_by or None
            changed = True

        completion = session_payload.get("completion")
        if isinstance(completion, dict):
            completion_changed = False
            discoverers = replace_list(completion.get("discoverers", []))
            if discoverers != list(completion.get("discoverers", [])):
                completion["discoverers"] = discoverers
                completion_changed = True
            completion_owner = replace_value(completion.get("owner_username"))
            if completion_owner != str(completion.get("owner_username") or "").strip():
                completion["owner_username"] = completion_owner
                completion_changed = True
            if completion_changed:
                changed = True

        return changed

    try:
        if merged_invites:
            save_json(user_invites_key(new_username), merged_invites)

        for invite_session_id in merged_invites.keys():
            normalized_session_id = str(invite_session_id or "").strip()
            if not normalized_session_id:
                continue
            session_payload = load_json(session_key(normalized_session_id))
            if not isinstance(session_payload, dict):
                continue
            if replace_session_payload(session_payload):
                save_json(session_key(normalized_session_id), session_payload)

        delete_keys(user_invites_key(current_username), user_session_key(current_username))
    except HTTPException:
        pass


def _load_user_or_404(username: str) -> dict[str, Any]:
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")
    return user


def _prune_account_lite_cache_locked(now_monotonic: float) -> None:
    expired_users = [
        cache_key
        for cache_key, (expires_at, _) in _account_lite_cache.items()
        if now_monotonic >= float(expires_at or 0.0)
    ]
    for cache_key in expired_users:
        _account_lite_cache.pop(cache_key, None)

    if len(_account_lite_cache) <= MAX_ACCOUNT_LITE_CACHE_ENTRIES:
        return

    overflow = len(_account_lite_cache) - MAX_ACCOUNT_LITE_CACHE_ENTRIES
    oldest_users = sorted(
        _account_lite_cache.items(),
        key=lambda entry: float(entry[1][0] or 0.0),
    )[:overflow]
    for cache_key, _ in oldest_users:
        _account_lite_cache.pop(cache_key, None)


def _get_cached_lite_account_payload(username: str) -> dict[str, Any] | None:
    cache_key = str(username or "").strip().casefold()
    if not cache_key:
        return None

    now_monotonic = time.monotonic()
    cached = _account_lite_cache.get(cache_key)
    if cached and now_monotonic < float(cached[0] or 0.0):
        return cached[1]

    with _account_lite_cache_lock:
        now_monotonic = time.monotonic()
        _prune_account_lite_cache_locked(now_monotonic)
        cached = _account_lite_cache.get(cache_key)
        if cached and now_monotonic < float(cached[0] or 0.0):
            return cached[1]

    return None


def _set_cached_lite_account_payload(username: str, payload: dict[str, Any]) -> None:
    cache_key = str(username or "").strip().casefold()
    if not cache_key:
        return

    now_monotonic = time.monotonic()
    with _account_lite_cache_lock:
        _prune_account_lite_cache_locked(now_monotonic)
        _account_lite_cache[cache_key] = (
            now_monotonic + ACCOUNT_LITE_CACHE_TTL_SECONDS,
            payload,
        )


def _sync_user(username: str, include_map_sync: bool = True) -> dict[str, Any]:
    user = _load_user_or_404(username)

    set_updates = build_user_defaults_update(user)
    owned_to_add: list[Any] = []
    owned_to_remove: list[Any] = []
    if include_map_sync:
        owned_to_add, owned_to_remove = build_owned_maps_sync_update(user, maps_collection)
    update_query: dict[str, Any] = {}
    if set_updates:
        update_query["$set"] = set_updates
    if owned_to_add:
        update_query.setdefault("$addToSet", {})["maps_owned"] = {"$each": owned_to_add}
    if owned_to_remove:
        update_query.setdefault("$pull", {})["maps_owned"] = {"$in": owned_to_remove}

    if update_query:
        users_collection.update_one({"_id": user["_id"]}, update_query)
        user = users_collection.find_one({"_id": user["_id"]}) or user

    return user


def _serialize_inventory_items(user: dict[str, Any]) -> list[dict[str, Any]]:
    item_counts = user.get("item_counts", {}) or {}
    owned_cosmetics = {
        str(item_id).strip()
        for item_id in user.get("owned_cosmetics", []) or []
        if str(item_id).strip()
    }
    item_ids = {
        str(item_id).strip()
        for item_id, count in item_counts.items()
        if str(item_id).strip() and int(count or 0) > 0
    }
    item_ids.update(owned_cosmetics)

    if not item_ids:
        return []

    docs = list(item_inventory.find({"item_id": {"$in": list(item_ids)}}))
    doc_lookup = {doc.get("item_id"): doc for doc in docs}
    inventory: list[dict[str, Any]] = []
    for item_id in sorted(item_ids):
        doc = doc_lookup.get(item_id)
        if not doc:
            continue
        if not is_item_supported_for_current_app(doc):
            continue

        serialized = serialize_shop_item(doc)
        serialized["count"] = max(int(item_counts.get(item_id, 0) or 0), 1 if item_id in owned_cosmetics else 0)
        serialized["is_owned"] = serialized["count"] > 0
        inventory.append(serialized)

    return inventory


@router.get("/account")
@limiter.limit("30/minute")
def get_account(request: Request, username: str, include_maps: bool = True):
    normalized_username = validate_login_username(username, field_name="username")
    touch_user_presence(users_collection, normalized_username)
    include_maps = bool(include_maps)
    if include_maps:
        user = _sync_user(normalized_username, include_map_sync=True)
        return {"status": "success", "user": serialize_session_user(user, maps_collection, include_maps=True)}

    cached_payload = _get_cached_lite_account_payload(normalized_username)
    if cached_payload is not None:
        return cached_payload

    # Lightweight polling path: avoid sync writes and map ownership checks.
    user = _load_user_or_404(normalized_username)
    payload = {"status": "success", "user": serialize_session_user(user, maps_collection, include_maps=False)}
    _set_cached_lite_account_payload(normalized_username, payload)
    return payload


@router.put("/update_email")
@limiter.limit("10/minute")
def update_email(request: Request, payload: UpdateEmailPayload):
    current_username = validate_login_username(payload.username, field_name="username")
    user = _sync_user(current_username)
    _verify_password(user, payload.current_password)

    next_email = validate_email_address(payload.new_email, field_name="new_email")

    normalized = normalize_email(next_email)
    existing = users_collection.find_one({"email_normalized": normalized, "username": {"$ne": current_username}})
    if existing:
        raise HTTPException(status_code=409, detail="Email is already in use")

    users_collection.update_one(
        {"_id": user["_id"]},
        {"$set": {"email": next_email, "email_normalized": normalized}},
    )

    refreshed = _sync_user(current_username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.put("/update_password")
@limiter.limit("10/minute")
def update_password(request: Request, payload: UpdatePasswordPayload):
    current_username = validate_login_username(payload.username, field_name="username")
    user = _sync_user(current_username)
    _verify_password(user, payload.current_password)
    new_password = validate_password_strength(payload.new_password, field_name="new_password")

    hashed_password = bcrypt.hashpw(new_password.encode("utf-8"), bcrypt.gensalt()).decode("utf-8")
    users_collection.update_one({"_id": user["_id"]}, {"$set": {"password": hashed_password}})

    refreshed = _sync_user(current_username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.put("/update_username")
@limiter.limit("5/minute")
def update_username(request: Request, payload: UpdateUsernamePayload):
    current_username = validate_login_username(payload.username, field_name="username")
    new_username = validate_username(payload.new_username, field_name="new_username")
    if new_username.lower() == SYSTEM_BANK_USERNAME:
        raise HTTPException(status_code=403, detail="That username is reserved")
    if new_username.casefold() == current_username.casefold():
        raise HTTPException(status_code=400, detail="Choose a different username")

    user = _sync_user(current_username)
    if user.get("is_system_account"):
        raise HTTPException(status_code=403, detail="System accounts cannot change usernames")
    _verify_password(user, payload.current_password)
    _assert_username_cooldown_elapsed(user)
    _assert_username_change_multiplayer_safe(current_username)

    existing_user = users_collection.find_one(
        {
            "_id": {"$ne": user["_id"]},
            "username": {"$regex": f"^{re.escape(new_username)}$", "$options": "i"},
        }
    )
    if existing_user:
        raise HTTPException(status_code=409, detail="Username is already in use")

    users_collection.update_one(
        {"_id": user["_id"]},
        {"$set": {"username": new_username, "last_username_change_at": datetime.now(timezone.utc)}},
    )

    affected_users = users_collection.find(
        {
            "_id": {"$ne": user["_id"]},
            "$or": [
                {"friends": current_username},
                {"friend_requests": current_username},
            ],
        },
        {
            "friends": 1,
            "friend_requests": 1,
        },
    )
    for affected_user in affected_users:
        updated_friends = _replace_username_values(affected_user.get("friends", []), current_username, new_username)
        updated_friend_requests = _replace_username_values(
            affected_user.get("friend_requests", []),
            current_username,
            new_username,
        )
        users_collection.update_one(
            {"_id": affected_user["_id"]},
            {
                "$set": {
                    "friends": updated_friends,
                    "friend_requests": updated_friend_requests,
                }
            },
        )

    maps_collection.update_many({"owner": current_username}, {"$set": {"owner": new_username}})
    maps_collection.update_many({"founder": current_username}, {"$set": {"founder": new_username}})
    maps_collection.update_many({"user_with_best_time": current_username}, {"$set": {"user_with_best_time": new_username}})
    marketplace_collection.update_many({"seller": current_username}, {"$set": {"seller": new_username}})
    run_results.update_many({"username": current_username}, {"$set": {"username": new_username}})
    _migrate_multiplayer_user_keys(current_username, new_username)

    refreshed = _sync_user(new_username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.put("/update_avatar")
@limiter.limit("20/minute")
def update_avatar(request: Request, payload: UpdateAvatarPayload):
    current_username = validate_login_username(payload.username, field_name="username")
    map_name = ensure_safe_text(payload.map_name, field_name="map_name", min_length=1, max_length=120)
    user = _sync_user(current_username)
    if user.get("is_system_account"):
        raise HTTPException(status_code=403, detail="System accounts cannot change avatars")

    owned_map_docs, _ = resolve_user_maps(user, maps_collection)
    owned_map = next(
        (
            map_doc
            for map_doc in owned_map_docs
            if str(map_doc.get("map_name") or "").strip().lower() == map_name.lower()
        ),
        None,
    )
    if not owned_map or not owned_map.get("map_image"):
        raise HTTPException(status_code=404, detail="Only owned maps with images can be used as profile pictures")

    profile_image = {
        "map_name": map_name,
        "image_url": owned_map.get("map_image"),
        "crop": dict(DEFAULT_AVATAR_CROP),
        "updated_at": datetime.now(timezone.utc).isoformat(),
    }

    users_collection.update_one({"_id": user["_id"]}, {"$set": {"profile_image": profile_image}})
    refreshed = _sync_user(current_username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.delete("/delete_account")
@limiter.limit("5/minute")
def delete_account(request: Request, payload: DeleteAccountPayload):
    username = validate_login_username(payload.username, field_name="username")
    confirm_username = validate_login_username(payload.confirm_username, field_name="confirm_username")
    if confirm_username.casefold() != username.casefold():
        raise HTTPException(status_code=400, detail="Username confirmation does not match")

    user = _sync_user(username)
    _verify_password(user, payload.current_password)

    result = delete_user_account(username)
    return {"status": "success", **result}


@router.post("/remove_friend")
@limiter.limit("20/minute")
def remove_friend(request: Request, payload: RemoveFriendPayload):
    username = validate_login_username(payload.username, field_name="username")
    friend_username = validate_login_username(payload.friend_username, field_name="friend_username")

    users_collection.update_one(
        {"username": username},
        {"$pull": {"friends": friend_username, "friend_requests": friend_username}},
    )
    users_collection.update_one(
        {"username": friend_username},
        {"$pull": {"friends": username, "friend_requests": username}},
    )

    refreshed = _sync_user(username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.post("/tutorial_state")
@limiter.limit("20/minute")
def update_tutorial_state(request: Request, payload: TutorialStatePayload):
    username = validate_login_username(payload.username, field_name="username")
    action = ensure_safe_text(payload.action, field_name="action", min_length=1, max_length=32).strip().lower()
    if action not in {"seen", "completed", "skipped"}:
        raise HTTPException(status_code=400, detail="Action must be one of: seen, completed, skipped")

    user = _sync_user(username)
    tutorial_state = user.get("tutorial_state") if isinstance(user.get("tutorial_state"), dict) else {}
    tutorial_state = {
        "version": int(tutorial_state.get("version", 1) or 1),
        "seen_at": tutorial_state.get("seen_at"),
        "completed_at": tutorial_state.get("completed_at"),
        "skipped_at": tutorial_state.get("skipped_at"),
    }

    now = datetime.now(timezone.utc).isoformat()
    if action == "seen":
        tutorial_state["seen_at"] = tutorial_state.get("seen_at") or now
    elif action == "completed":
        tutorial_state["seen_at"] = tutorial_state.get("seen_at") or now
        tutorial_state["completed_at"] = now
    elif action == "skipped":
        tutorial_state["seen_at"] = tutorial_state.get("seen_at") or now
        tutorial_state["skipped_at"] = now

    users_collection.update_one({"_id": user["_id"]}, {"$set": {"tutorial_state": tutorial_state}})
    refreshed = _sync_user(username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.get("/inventory")
@limiter.limit("30/minute")
def get_inventory(request: Request, username: str):
    normalized_username = validate_login_username(username, field_name="username")
    user = _sync_user(normalized_username)
    return {
        "status": "success",
        "username": normalized_username,
        "items": _serialize_inventory_items(user),
    }


@router.post("/abandon_run")
@limiter.limit("60/minute")
def abandon_run(request: Request, payload: AbandonRunPayload):
    username = validate_login_username(payload.username, field_name="username")
    run_nonce = ensure_safe_text(payload.run_nonce, field_name="run_nonce", min_length=1, max_length=256)
    seed = ensure_safe_text(payload.seed, field_name="seed", min_length=1, max_length=2048)
    source = ensure_safe_text(payload.source, field_name="source", min_length=1, max_length=32)
    reason = ensure_safe_text(payload.reason, field_name="reason", min_length=1, max_length=64)
    map_name = ensure_safe_text(payload.map_name, field_name="map_name", required=False, max_length=120)
    used_items = [
        ensure_safe_text(item_id, field_name="used_item", min_length=1, max_length=64)
        for item_id in payload.used_items
    ]

    user = _sync_user(username)
    if user.get("is_system_account") or username == SYSTEM_BANK_USERNAME:
        raise HTTPException(status_code=403, detail="System accounts cannot submit runs")

    run_results.create_index("run_nonce", unique=True)
    if run_results.find_one({"run_nonce": run_nonce}):
        refreshed_user = _sync_user(username)
        return {"status": "success", "already_processed": True, "user": serialize_session_user(refreshed_user, maps_collection)}

    item_counts = user.get("item_counts", {})
    requested_use_counts: dict[str, int] = {}
    for item_id in used_items:
        requested_use_counts[item_id] = requested_use_counts.get(item_id, 0) + 1

    for item_id, amount in requested_use_counts.items():
        if int(item_counts.get(item_id, 0) or 0) < amount:
            raise HTTPException(status_code=400, detail=f"Insufficient item quantity: {item_id}")

    loss_fee = compute_loss_fee(user)

    inc_query: dict[str, int] = {
        "number_of_maps_played": 1,
        "maps_lost": 1,
    }
    if loss_fee["applied_fee"] > 0:
        inc_query["maze_nuggets"] = -loss_fee["applied_fee"]
    for item_id, amount in requested_use_counts.items():
        inc_query[f"item_counts.{item_id}"] = -amount

    update_filter: dict[str, Any] = {"_id": user["_id"]}
    if loss_fee["applied_fee"] > 0:
        update_filter["maze_nuggets"] = {"$gte": loss_fee["applied_fee"]}
    for item_id, amount in requested_use_counts.items():
        update_filter[f"item_counts.{item_id}"] = {"$gte": amount}

    try:
        with client.start_session() as session:
            with session.start_transaction():
                update_result = users_collection.update_one(update_filter, {"$inc": inc_query}, session=session)
                if update_result.modified_count != 1:
                    raise HTTPException(status_code=409, detail="Unable to record this abandoned run")

                if loss_fee["applied_fee"] > 0:
                    credit_bank_dividend(users_collection, loss_fee["applied_fee"], session)

                run_results.insert_one(
                    {
                        "run_nonce": run_nonce,
                        "username": username,
                        "seed": seed,
                        "map_name": map_name,
                        "source": source,
                        "reason": reason,
                        "used_items": used_items,
                        "forfeited_run_payout": int(payload.forfeited_run_payout or 0),
                        "projected_completion_payout": int(payload.projected_completion_payout or 0),
                        "map_value": int(payload.map_value or 0),
                        "loss_fee_applied": int(loss_fee["applied_fee"]),
                        "created_at": datetime.now(timezone.utc),
                    },
                    session=session,
                )
    except DuplicateKeyError:
        refreshed_user = _sync_user(username)
        return {"status": "success", "already_processed": True, "user": serialize_session_user(refreshed_user, maps_collection)}

    refreshed = _sync_user(username)
    return {
        "status": "success",
        "already_processed": False,
        "loss_fee_applied": loss_fee["applied_fee"],
        "user": serialize_session_user(refreshed, maps_collection),
    }
