from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

import bcrypt
from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, Field
from pymongo.errors import DuplicateKeyError

from main import limiter

from .account_cleanup import delete_user_account
from .db import client, item_inventory, maps_collection, run_results, users_collection
from .economy_rules import compute_loss_fee, credit_bank_dividend
from .item_catalog import serialize_shop_item
from .user_utils import (
    SYSTEM_BANK_USERNAME,
    build_owned_maps_sync_update,
    build_user_defaults_update,
    normalize_email,
    resolve_user_maps,
    serialize_session_user,
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
