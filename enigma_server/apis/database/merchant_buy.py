from fastapi import APIRouter, HTTPException, Request
from pymongo.errors import PyMongoError

from .db import app_token, client, item_inventory, merchant, users_collection
from decoder import decode
from main import limiter


router = APIRouter(prefix="/database/merchant")


@router.post("/buy_item")
@limiter.limit("10/minute")
def buy_item(request: Request, username: str, item_id: str, token: str, quantity: int = 1):
    import hmac

    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(status_code=401, detail="Invalid token")

    if quantity < 1:
        raise HTTPException(status_code=400, detail="Quantity must be at least 1")

    try:
        with client.start_session() as session:
            with session.start_transaction():
                item = merchant.find_one({"item_id": item_id}, session=session)
                if not item:
                    raise HTTPException(status_code=404, detail="Item not found in shop")

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

                shop_result = merchant.update_one(
                    {"item_id": item_id, "stock": {"$gte": quantity}},
                    {"$inc": {"stock": -quantity}},
                    session=session,
                )
                if shop_result.modified_count != 1:
                    raise HTTPException(status_code=409, detail="Item is out of stock")

                inv_result = item_inventory.update_one(
                    {"item_id": item_id, "stock": {"$gte": quantity}},
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
                        raise HTTPException(status_code=400, detail="Not enough maze nuggets")
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
