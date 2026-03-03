from fastapi import APIRouter, HTTPException, Request

from .db import users_collection, maps_collection, item_inventory
from .economy_rules import compute_loss_fee, compute_single_player_reward, credit_bank_dividend
from main import limiter

router = APIRouter(prefix="/database/users")


@router.put("/update_progress")
@limiter.limit("1/minute")
def update_user_progress(
    request: Request,
    username: str,
    map_seed: str,
    items_in_use: str,
    earned_mn: int,
    seed_existed: bool = True,
    map_lost: bool = False,
):


    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    map_doc = maps_collection.find_one({"seed": map_seed})
    if not map_doc:
        raise HTTPException(status_code=404, detail="Map not found")
    map_id = map_doc["_id"]

    if earned_mn < 0:
        raise HTTPException(status_code=400, detail="earned_mn cannot be negative")

    parsed_items = []
    raw_items = (items_in_use or "").strip()
    if raw_items and raw_items.lower() not in ("none", "null"):
        parsed_items = [item.strip() for item in raw_items.split(",") if item.strip()]

    item_counts = user.get("item_counts", {})
    requested_use_counts = {}
    for item_id in parsed_items:
        requested_use_counts[item_id] = requested_use_counts.get(item_id, 0) + 1

    for item_id, needed in requested_use_counts.items():
        if int(item_counts.get(item_id, 0)) < needed:
            raise HTTPException(
                status_code=400,
                detail=f"Item not owned or insufficient quantity: {item_id}",
            )

    reward_multiplier = 1.0
    for item_id in parsed_items:
        item_doc = item_inventory.find_one({"item_id": item_id})
        if not item_doc:
            raise HTTPException(status_code=404, detail=f"Item not found: {item_id}")
        effect = item_doc.get("effect", {})
        if effect.get("type") == "reward_multiplier":
            try:
                effect_value = float(effect.get("value", 1))
            except (TypeError, ValueError):
                effect_value = 1.0
            if effect_value > 0:
                reward_multiplier *= effect_value

    gross_reward = int(earned_mn * reward_multiplier)
    economy = compute_single_player_reward(gross_reward, user)
    rewarded_mn = economy["rewarded_mn"]
    loss_fee = compute_loss_fee(user) if map_lost else {"applied_fee": 0}

    update_query = {"$inc": {"number_of_maps_played": 1}}
    update_query["$inc"]["maze_nuggets"] = rewarded_mn - int(loss_fee["applied_fee"])

    if map_lost:
        update_query["$inc"]["maps_lost"] = 1
    else:
        update_query["$inc"]["maps_completed"] = 1

    for item_id, amount in requested_use_counts.items():
        update_query["$inc"][f"item_counts.{item_id}"] = -amount

    if not seed_existed:
        update_query["$addToSet"] = {"maps_discovered": map_id, "maps_owned": map_id}

    result = users_collection.update_one({"username": username}, update_query)
    credit_bank_dividend(users_collection, economy["bank_dividend"] + int(loss_fee["applied_fee"]))

    return {
        "status": "success",
        "modified_count": result.modified_count,
        "rewarded_mn": rewarded_mn,
        "reward_multiplier": reward_multiplier,
        "gross_reward": gross_reward,
        "bank_dividend": economy["bank_dividend"],
        "loss_fee_applied": int(loss_fee["applied_fee"]),
        "founders_multiplier": economy["reward_multiplier"],
    }
