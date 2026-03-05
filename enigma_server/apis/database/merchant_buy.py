from fastapi import APIRouter, HTTPException, Request
from pymongo.errors import PyMongoError
from .db import client, item_inventory, maps_collection, merchant, users_collection
from .economy_rules import credit_bank_dividend
from .founders_mark import FOUNDERS_MARK_ITEM_ID, evaluate_founders_mark_requirements
from .item_catalog import is_item_supported_for_current_app
from .system_accounts import ensure_bank_account
from main import limiter


router = APIRouter(prefix="/database/merchant")


@router.post("/buy_item")
@limiter.limit("10/minute")
def buy_item(request: Request, username: str, item_id: str, quantity: int = 1):
    if quantity < 1:
        raise HTTPException(status_code=400, detail="Quantity must be at least 1")

    ensure_bank_account()

    try:
        with client.start_session() as session:
            with session.start_transaction():
                item = merchant.find_one({"item_id": item_id}, session=session)
                from_daily_shop = item is not None
                if not item:
                    item = item_inventory.find_one(
                        {
                            "item_id": item_id,
                            "always_available": True,
                            "stock": {"$gt": 0},
                            "retired": {"$ne": True},
                        },
                        session=session,
                    )
                if not item:
                    raise HTTPException(status_code=404, detail="Item not found in shop")
                if not is_item_supported_for_current_app(item):
                    raise HTTPException(status_code=409, detail="This item is not available in the current app version")

                category = item.get("category")
                price = item.get("price")
                if not isinstance(price, (int, float)) or price < 0:
                    raise HTTPException(status_code=500, detail="Invalid item price")

                if category == "cosmetic" and quantity != 1:
                    raise HTTPException(
                        status_code=400,
                        detail="Cosmetic items can only be bought one at a time",
                    )

                total_cost = int(price * quantity)

                user = users_collection.find_one({"username": username}, session=session)
                if not user:
                    raise HTTPException(status_code=404, detail="User not found")

                purchase_limit = int(item.get("purchase_limit", 0) or 0)
                already_owned_count = int((user.get("item_counts", {}) or {}).get(item_id, 0) or 0)
                if purchase_limit > 0 and already_owned_count >= purchase_limit:
                    raise HTTPException(status_code=409, detail="You already claimed this item")

                if item_id == FOUNDERS_MARK_ITEM_ID:
                    if quantity != 1:
                        raise HTTPException(status_code=400, detail="Founders Mark can only be claimed once")

                    eligibility = evaluate_founders_mark_requirements(user, maps_collection)
                    if not eligibility["eligible"]:
                        raise HTTPException(
                            status_code=403,
                            detail=eligibility["reason"] or "You have not completed the Founders Mark checklist",
                        )

                if from_daily_shop:
                    shop_result = merchant.update_one(
                        {"item_id": item_id, "stock": {"$gte": quantity}},
                        {"$inc": {"stock": -quantity}},
                        session=session,
                    )
                    if shop_result.modified_count != 1:
                        raise HTTPException(status_code=409, detail="Item is out of stock")

                inv_result = item_inventory.update_one(
                    {"item_id": item_id, "stock": {"$gte": quantity}, "retired": {"$ne": True}},
                    {"$inc": {"stock": -quantity}},
                    session=session,
                )
                if inv_result.modified_count != 1:
                    raise HTTPException(status_code=409, detail="Inventory stock mismatch")

                if category == "cosmetic":
                    buyer_result = users_collection.update_one(
                        {
                            "username": username,
                            "maze_nuggets": {"$gte": total_cost},
                            "owned_cosmetics": {"$ne": item_id},
                        },
                        {
                            "$inc": {"maze_nuggets": -total_cost},
                            "$addToSet": {"owned_cosmetics": item_id},
                        },
                        session=session,
                    )
                    if buyer_result.modified_count != 1:
                        user_after = users_collection.find_one(
                            {"username": username},
                            session=session,
                        )
                        if user_after and item_id in user_after.get("owned_cosmetics", []):
                            raise HTTPException(status_code=409, detail="You already own this cosmetic")
                        raise HTTPException(status_code=400, detail="Not enough maze nuggets")
                else:
                    buyer_result = users_collection.update_one(
                        {"username": username, "maze_nuggets": {"$gte": total_cost}},
                        {
                            "$inc": {
                                "maze_nuggets": -total_cost,
                                f"item_counts.{item_id}": quantity,
                            }
                        },
                        session=session,
                    )
                    if buyer_result.modified_count != 1:
                        if total_cost == 0:
                            raise HTTPException(status_code=409, detail="Unable to claim this item")
                        raise HTTPException(status_code=400, detail="Not enough maze nuggets")

                if total_cost > 0:
                    credit_bank_dividend(users_collection, total_cost, session)
    except HTTPException:
        raise
    except PyMongoError:
        raise HTTPException(status_code=500, detail="Failed to complete purchase transaction")

    return {
        "status": "success",
        "item_id": item_id,
        "quantity": quantity,
        "total_cost": int(price * quantity),
    }
