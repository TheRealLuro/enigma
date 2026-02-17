from fastapi import APIRouter, HTTPException, Request
from .db import maps_collection, marketplace_collection, app_token
from datetime import datetime
from main import limiter
from decoder import decode


router = APIRouter(prefix="/database/maps")


@router.post("/add_to_marketplace")
@limiter.limit("5/minute")
def add_to_market(request: Request, map_name: str, price: int, token: str):


    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)
    
    map = maps_collection.find_one({"map_name": map_name})

    value = map['value']

    sfl = map['sold_for_last']

    if value != 0:
        increase_from_value = price / value 
    
    if sfl != 0:
        increase_from_sfl = price / sfl

    listing = {
        "map_details": map,
        "price": price, 
        "seller": map['owner'],
        "sold_for_last": sfl,
        "listed_at": datetime.now(datetime.timezone.utc),
        "last_bought" : map['last_bought']
    }

    marketplace_collection.insert_one(listing)

    return {"Success": f"You added the listing to the marketplace, You listed for {increase_from_value}x the value and {increase_from_sfl}x the sold last value"}

        