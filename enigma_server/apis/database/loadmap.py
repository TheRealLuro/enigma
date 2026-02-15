from fastapi import APIRouter, HTTPException, Request
from .db import maps_collection, app_token
from main import limiter
from decoder import decode


router = APIRouter(prefix="/database/maps")


@router.get("/load_map")
@limiter.limit("1/minute")
def load_map(
    request: Request,
    map_name: str,
    token: str, 
):
    
    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)
    
    map_data = maps_collection.find_one({"map_name": map_name})

    if not map_data:
        raise HTTPException(status_code=404, detail="Map not found")


    return {"status": "success", "seed": map_data["seed"]}
