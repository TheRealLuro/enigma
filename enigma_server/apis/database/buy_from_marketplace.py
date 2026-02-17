from fastapi import APIRouter, HTTPException, Request
from .db import marketplace_collection, app_token, users_collection, maps_collection
from datetime import datetime, timezone
from main import limiter
from decoder import decode


router = APIRouter(prefix="/database/marketplace")


@router.post("/buy")
@limiter.limit("5/minute")
def buy_from(request: Request, map_name: str, buyer: str, token: str):

    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)
    
    listing = marketplace_collection.find_one_and_delete({'map_name': map_name})

    if not listing:
        raise HTTPException(status_code=404, detail="Listing not found")

    if buyer == listing['seller']:
        raise HTTPException(status_code=400, detail="You cannot buy your own maps")
    
    buyer_user = users_collection.find_one({'username': buyer})
    seller_username = listing['seller']
    seller = users_collection.find_one({'username': seller_username})
    map_doc = maps_collection.find_one({'map_name': map_name})

    if not buyer_user:
        raise HTTPException(status_code=404, detail="Buyer not found")
    if not seller:
        raise HTTPException(status_code=404, detail="Seller not found")
    if not map_doc:
        raise HTTPException(status_code=404, detail="Map not found")

    cost = listing['price']
    sfl = listing['price']
    last_bought = datetime.now(timezone.utc)

    buyer_result = users_collection.update_one(
    {"username": buyer_user["username"], "maze_nuggets": {"$gte": cost}},
    {"$inc": {"maze_nuggets": -cost}, "$addToSet": {"maps_owned": map_doc["_id"]}}
    )

    if buyer_result.modified_count == 0:
        marketplace_collection.insert_one(listing)
        raise HTTPException(status_code=400, detail="You do not have enough maze nuggets")


    seller_result = users_collection.update_one(
    {"username": seller_username},
    {"$inc": {"maze_nuggets": cost}, "$pull": {"maps_owned": map_doc["_id"]}}
    )

    if seller_result.matched_count == 0:
        raise HTTPException(status_code=404, detail="Seller not found")

    map_result = maps_collection.update_one(
    {"map_name": map_name},
    {"$set": {"owner": buyer_user["username"], "sold_for_last": sfl, "last_bought": last_bought}}
    )
    
    if map_result.matched_count == 0:
        raise HTTPException(status_code=404, detail="Map not found")

    

    return {"status": "success"}