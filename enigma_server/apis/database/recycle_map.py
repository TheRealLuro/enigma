from __future__ import annotations

from fastapi import APIRouter, HTTPException, Request

from main import limiter

from .db import maps_collection, marketplace_collection, users_collection
from .economy_rules import credit_bank_dividend
from .system_accounts import ensure_bank_account
from .user_utils import SYSTEM_BANK_USERNAME

router = APIRouter(prefix="/database/maps")


@router.post("/recycle")
@limiter.limit("10/minute")
def recycle_map(request: Request, username: str, map_name: str):
    ensure_bank_account()

    if not map_name.strip():
        raise HTTPException(status_code=400, detail="Map name is required")

    if username.strip().lower() == SYSTEM_BANK_USERNAME:
        raise HTTPException(status_code=403, detail="The bank account cannot recycle maps")

    map_doc = maps_collection.find_one({"map_name": map_name, "owner": username})
    if not map_doc:
        raise HTTPException(status_code=404, detail="Only the current owner can recycle this map")

    bank_user = users_collection.find_one({"username": SYSTEM_BANK_USERNAME})
    if not bank_user:
        raise HTTPException(status_code=500, detail="Enigma bank account is unavailable")

    map_value = int(map_doc.get("value", 0) or 0)
    payout_to_user = int(round(map_value * 0.70))
    payout_to_bank = max(0, map_value - payout_to_user)
    map_id = map_doc["_id"]

    users_collection.update_one(
        {"username": username},
        {
            "$inc": {"maze_nuggets": payout_to_user},
            "$pull": {"maps_owned": map_id},
        },
    )
    credit_bank_dividend(users_collection, payout_to_bank)
    users_collection.update_one(
        {"username": SYSTEM_BANK_USERNAME},
        {
            "$addToSet": {"maps_owned": map_id},
        },
    )
    users_collection.update_many(
        {"username": username, "profile_image.map_name": map_name},
        {"$set": {"profile_image": None}},
    )

    maps_collection.update_one(
        {"_id": map_id},
        {"$set": {"owner": SYSTEM_BANK_USERNAME, "sold_for_last": payout_to_user}},
    )
    marketplace_collection.delete_many({"map_name": map_name})

    return {
        "status": "success",
        "map_name": map_name,
        "map_value": map_value,
        "credited_to_user": payout_to_user,
        "credited_to_bank": payout_to_bank,
    }
