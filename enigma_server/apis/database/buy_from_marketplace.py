from datetime import datetime, timezone

from fastapi import APIRouter, HTTPException, Request
from pymongo.errors import PyMongoError

from main import limiter

from .db import client, marketplace_collection, maps_collection, users_collection
from .economy_rules import compute_marketplace_sale_split, credit_bank_dividend
from .system_accounts import ensure_bank_account


router = APIRouter(prefix="/database/marketplace")


@router.post("/buy")
@limiter.limit("5/minute")
def buy_from(request: Request, map_name: str, buyer: str):
    normalized_map_name = (map_name or "").strip()
    normalized_buyer = (buyer or "").strip()
    if not normalized_map_name:
        raise HTTPException(status_code=400, detail="Map name is required")
    if not normalized_buyer:
        raise HTTPException(status_code=400, detail="Buyer is required")

    ensure_bank_account()

    try:
        with client.start_session() as session:
            with session.start_transaction():
                listing = marketplace_collection.find_one({"map_name": normalized_map_name}, session=session)
                if not listing:
                    raise HTTPException(status_code=404, detail="Listing not found")

                seller_username = str(listing.get("seller") or "").strip()
                if normalized_buyer == seller_username:
                    raise HTTPException(status_code=400, detail="You cannot buy your own maps")

                buyer_user = users_collection.find_one({"username": normalized_buyer}, session=session)
                seller = users_collection.find_one({"username": seller_username}, session=session)
                map_doc = maps_collection.find_one({"map_name": normalized_map_name}, session=session)

                if not buyer_user:
                    raise HTTPException(status_code=404, detail="Buyer not found")
                if not seller:
                    raise HTTPException(status_code=404, detail="Seller not found")
                if not map_doc:
                    raise HTTPException(status_code=404, detail="Map not found")

                cost = int(listing.get("price", 0) or 0)
                if cost < 0:
                    raise HTTPException(status_code=400, detail="Listing price is invalid")

                split = compute_marketplace_sale_split(cost)
                map_id = map_doc["_id"]
                last_bought = datetime.now(timezone.utc)

                buyer_result = users_collection.update_one(
                    {
                        "username": normalized_buyer,
                        "maze_nuggets": {"$gte": cost},
                    },
                    {
                        "$inc": {"maze_nuggets": -cost},
                        "$addToSet": {
                            "maps_owned": map_id,
                            "maps_discovered": map_id,
                        },
                    },
                    session=session,
                )

                if buyer_result.modified_count != 1:
                    raise HTTPException(status_code=400, detail="You do not have enough maze nuggets")

                seller_result = users_collection.update_one(
                    {"username": seller_username},
                    {
                        "$inc": {"maze_nuggets": split["seller_reward"]},
                        "$pull": {"maps_owned": map_id},
                        "$addToSet": {"maps_discovered": map_id},
                    },
                    session=session,
                )

                if seller_result.matched_count != 1:
                    raise HTTPException(status_code=404, detail="Seller not found")

                if split["bank_dividend"] > 0:
                    credit_bank_dividend(users_collection, split["bank_dividend"], session)

                map_result = maps_collection.update_one(
                    {"_id": map_id},
                    {
                        "$set": {
                            "owner": normalized_buyer,
                            "sold_for_last": cost,
                            "last_bought": last_bought,
                        }
                    },
                    session=session,
                )

                if map_result.matched_count != 1:
                    raise HTTPException(status_code=404, detail="Map not found")

                marketplace_collection.delete_one({"_id": listing["_id"]}, session=session)
    except HTTPException:
        raise
    except PyMongoError:
        raise HTTPException(status_code=500, detail="Failed to complete marketplace transaction")

    return {
        "status": "success",
        "map_name": normalized_map_name,
        "price_paid": cost,
        "seller_reward": split["seller_reward"],
        "bank_dividend": split["bank_dividend"],
    }
