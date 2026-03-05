from __future__ import annotations

import re
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
from .item_catalog import serialize_shop_item
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

DEFAULT_AVATAR_CROP = {
    "x": 11.0,
    "y": 17.0,
    "size": 73.0,
}


class UpdateEmailPayload(BaseModel):
    username: str
    current_password: str
    new_email: str


class UpdatePasswordPayload(BaseModel):
    username: str
    current_password: str
    new_password: str = Field(min_length=8)


class UpdateUsernamePayload(BaseModel):
    username: str
    current_password: str
    new_username: str = Field(min_length=3, max_length=32)


class UpdateAvatarPayload(BaseModel):
    username: str
    map_name: str


class DeleteAccountPayload(BaseModel):
    username: str
    current_password: str
    confirm_username: str


class TutorialStatePayload(BaseModel):
    username: str
    action: str


class RemoveFriendPayload(BaseModel):
    username: str
    friend_username: str


class AbandonRunPayload(BaseModel):
    username: str
    run_nonce: str
    seed: str
    used_items: list[str] = []
    map_name: str | None = None
    source: str = "new"
    forfeited_run_payout: int = 0
    projected_completion_payout: int = 0
    map_value: int = 0
    reason: str = "abandoned"


def _verify_password(user: dict[str, Any], password: str) -> None:
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


def _sync_user(username: str) -> dict[str, Any]:
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    set_updates = build_user_defaults_update(user)
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

        serialized = serialize_shop_item(doc)
        serialized["count"] = max(int(item_counts.get(item_id, 0) or 0), 1 if item_id in owned_cosmetics else 0)
        serialized["is_owned"] = serialized["count"] > 0
        inventory.append(serialized)

    return inventory


@router.get("/account")
@limiter.limit("30/minute")
def get_account(request: Request, username: str):
    touch_user_presence(users_collection, username)
    user = _sync_user(username)
    return {"status": "success", "user": serialize_session_user(user, maps_collection)}


@router.put("/update_email")
@limiter.limit("10/minute")
def update_email(request: Request, payload: UpdateEmailPayload):
    user = _sync_user(payload.username)
    _verify_password(user, payload.current_password)

    next_email = payload.new_email.strip()
    if not next_email:
        raise HTTPException(status_code=400, detail="Email is required")

    normalized = normalize_email(next_email)
    existing = users_collection.find_one({"email_normalized": normalized, "username": {"$ne": payload.username}})
    if existing:
        raise HTTPException(status_code=409, detail="Email is already in use")

    users_collection.update_one(
        {"_id": user["_id"]},
        {"$set": {"email": next_email, "email_normalized": normalized}},
    )

    refreshed = _sync_user(payload.username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.put("/update_password")
@limiter.limit("10/minute")
def update_password(request: Request, payload: UpdatePasswordPayload):
    user = _sync_user(payload.username)
    _verify_password(user, payload.current_password)

    hashed_password = bcrypt.hashpw(payload.new_password.encode("utf-8"), bcrypt.gensalt()).decode("utf-8")
    users_collection.update_one({"_id": user["_id"]}, {"$set": {"password": hashed_password}})

    refreshed = _sync_user(payload.username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.put("/update_username")
@limiter.limit("5/minute")
def update_username(request: Request, payload: UpdateUsernamePayload):
    current_username = payload.username.strip()
    new_username = payload.new_username.strip()
    if not current_username:
        raise HTTPException(status_code=400, detail="Current username is required")
    if not new_username:
        raise HTTPException(status_code=400, detail="New username is required")
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
    user = _sync_user(payload.username)
    if user.get("is_system_account"):
        raise HTTPException(status_code=403, detail="System accounts cannot change avatars")

    owned_map_docs, _ = resolve_user_maps(user, maps_collection)
    owned_map = next(
        (
            map_doc
            for map_doc in owned_map_docs
            if str(map_doc.get("map_name") or "").strip().lower() == payload.map_name.strip().lower()
        ),
        None,
    )
    if not owned_map or not owned_map.get("map_image"):
        raise HTTPException(status_code=404, detail="Only owned maps with images can be used as profile pictures")

    profile_image = {
        "map_name": payload.map_name,
        "image_url": owned_map.get("map_image"),
        "crop": dict(DEFAULT_AVATAR_CROP),
        "updated_at": datetime.now(timezone.utc).isoformat(),
    }

    users_collection.update_one({"_id": user["_id"]}, {"$set": {"profile_image": profile_image}})
    refreshed = _sync_user(payload.username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.delete("/delete_account")
@limiter.limit("5/minute")
def delete_account(request: Request, payload: DeleteAccountPayload):
    if payload.confirm_username != payload.username:
        raise HTTPException(status_code=400, detail="Username confirmation does not match")

    user = _sync_user(payload.username)
    _verify_password(user, payload.current_password)

    result = delete_user_account(payload.username)
    return {"status": "success", **result}


@router.post("/remove_friend")
@limiter.limit("20/minute")
def remove_friend(request: Request, payload: RemoveFriendPayload):
    if not payload.friend_username.strip():
        raise HTTPException(status_code=400, detail="Friend username is required")

    users_collection.update_one(
        {"username": payload.username},
        {"$pull": {"friends": payload.friend_username, "friend_requests": payload.friend_username}},
    )
    users_collection.update_one(
        {"username": payload.friend_username},
        {"$pull": {"friends": payload.username, "friend_requests": payload.username}},
    )

    refreshed = _sync_user(payload.username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.post("/tutorial_state")
@limiter.limit("20/minute")
def update_tutorial_state(request: Request, payload: TutorialStatePayload):
    action = payload.action.strip().lower()
    if action not in {"seen", "completed", "skipped"}:
        raise HTTPException(status_code=400, detail="Unsupported tutorial action")

    user = _sync_user(payload.username)
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
    refreshed = _sync_user(payload.username)
    return {"status": "success", "user": serialize_session_user(refreshed, maps_collection)}


@router.get("/inventory")
@limiter.limit("30/minute")
def get_inventory(request: Request, username: str):
    user = _sync_user(username)
    return {
        "status": "success",
        "username": username,
        "items": _serialize_inventory_items(user),
    }


@router.post("/abandon_run")
@limiter.limit("60/minute")
def abandon_run(request: Request, payload: AbandonRunPayload):
    if not payload.run_nonce.strip():
        raise HTTPException(status_code=400, detail="Run nonce is required")

    user = _sync_user(payload.username)
    if user.get("is_system_account") or payload.username == SYSTEM_BANK_USERNAME:
        raise HTTPException(status_code=403, detail="System accounts cannot submit runs")

    run_results.create_index("run_nonce", unique=True)
    if run_results.find_one({"run_nonce": payload.run_nonce}):
        refreshed_user = _sync_user(payload.username)
        return {"status": "success", "already_processed": True, "user": serialize_session_user(refreshed_user, maps_collection)}

    item_counts = user.get("item_counts", {})
    requested_use_counts: dict[str, int] = {}
    for item_id in payload.used_items:
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
                        "run_nonce": payload.run_nonce,
                        "username": payload.username,
                        "seed": payload.seed,
                        "map_name": payload.map_name,
                        "source": payload.source,
                        "reason": payload.reason,
                        "used_items": payload.used_items,
                        "forfeited_run_payout": int(payload.forfeited_run_payout or 0),
                        "projected_completion_payout": int(payload.projected_completion_payout or 0),
                        "map_value": int(payload.map_value or 0),
                        "loss_fee_applied": int(loss_fee["applied_fee"]),
                        "created_at": datetime.now(timezone.utc),
                    },
                    session=session,
                )
    except DuplicateKeyError:
        refreshed_user = _sync_user(payload.username)
        return {"status": "success", "already_processed": True, "user": serialize_session_user(refreshed_user, maps_collection)}

    refreshed = _sync_user(payload.username)
    return {
        "status": "success",
        "already_processed": False,
        "loss_fee_applied": loss_fee["applied_fee"],
        "user": serialize_session_user(refreshed, maps_collection),
    }
