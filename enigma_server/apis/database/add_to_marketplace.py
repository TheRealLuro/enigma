from datetime import datetime, timezone
from fastapi import APIRouter, HTTPException, Request
from main import limiter
from .db import maps_collection, marketplace_collection, users_collection


router = APIRouter(prefix="/database/maps")


@router.post("/add_to_marketplace")
@limiter.limit("5/minute")
def add_to_market(request: Request, user: str, map_name: str, price: int):
    if price <= 0:
        raise HTTPException(status_code=400, detail="Price must be greater than zero")

    map = maps_collection.find_one({"map_name": map_name})
    if not map:
        raise HTTPException(status_code=404, detail="Map not found")

    if map["owner"] != user:
        raise HTTPException(status_code=400, detail="You do not own this map")
    user_doc = users_collection.find_one({"username": user}, {"staked_map_ids": 1})
    if not user_doc:
        raise HTTPException(status_code=404, detail="User not found")
    map_id = str(map.get("_id") or "").strip()
    staked_map_ids = {
        str(value or "").strip()
        for value in list(user_doc.get("staked_map_ids", []) or [])
        if str(value or "").strip()
    }
    if map_id and map_id in staked_map_ids:
        raise HTTPException(status_code=409, detail="Staked maps cannot be listed. Unstake this map first.")

    if marketplace_collection.find_one({"map_name": map_name}):
        raise HTTPException(status_code=400, detail="Map is already listed")

    sold_for_last = map.get("sold_for_last", 0)

    listing = {
        "map_name": map["map_name"],
        "map_image": map["map_image"],
        "image_status": map.get("image_status") or ("ready" if map.get("map_image") else "pending_upload"),
        "theme": map.get("theme"),
        "difficulty": map.get("difficulty"),
        "size": map.get("size"),
        "value": map.get("value", 0),
        "price": price,
        "seller": map["owner"],
        "sold_for_last": sold_for_last,
        "listed_at": datetime.now(timezone.utc),
        "last_bought": map.get("last_bought"),
    }

    marketplace_collection.insert_one(listing)

    return {"status": "success", "message": "Map listed"}

        
