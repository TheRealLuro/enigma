from fastapi import APIRouter, HTTPException, Request
from .db import maps_collection
from main import limiter


router = APIRouter(prefix="/database/maps")


@router.get("/load_map")
@limiter.limit("1/minute")
def load_map(
    request: Request,
    map_name: str, 
):
    
    
    map_data = maps_collection.find_one({"map_name": map_name})

    if not map_data:
        raise HTTPException(status_code=404, detail="Map not found")


    return {"status": "success", "seed": map_data["seed"]}
