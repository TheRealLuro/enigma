from fastapi import APIRouter, HTTPException, Request
from .db import maps_collection, marketplace_collection, app_token
from datetime import datetime, timezone
from main import limiter
from decoder import decode


router = APIRouter(prefix="/database/maps")


@router.post("/add_to_marketplace")
@limiter.limit("5/minute")
def add_to_market(request: Request, user:str, map_name: str, price: int):

    
    map = maps_collection.find_one({"map_name": map_name})
  
    if map["owner"] != user:
      raise HTTPException(status_code=400, detail="you dont own this")

    if marketplace_collection.find_one({"map_name": map_name}):
       raise HTTPException(status_code=400, detail="listed already")

    value = map['value']

    sfl = map['sold_for_last']


    listing = {
        "map_name": map["map_name"],
        "map_image": map["map_image"],
        "price": price, 
        "seller": map['owner'],
        "sold_for_last": sfl,
        "listed_at": datetime.now(timezone.utc),
        "last_bought" : map['last_bought']
    }

    marketplace_collection.insert_one(listing)

    return {"Success": f"listed"}

        