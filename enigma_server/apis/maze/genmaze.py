from fastapi import APIRouter, HTTPException, Request
from apis.maze.maze import get_seed
from ..database.db import app_token, maps_collection
from main import limiter
from decoder import decode


router = APIRouter(prefix="/maze")


@router.get("/genseed")
@limiter.limit("1/minute")
def return_seed(request: Request, difficulty: str, size: int, token: str):

    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)

    if size <= 1:
        raise HTTPException(status_code=400, detail="Invalid size must be greater than 1")
    
    seed_exists = True

    while seed_exists == True:
        raw_seed = get_seed(size)
        seed = f'{difficulty}-{raw_seed}'

        if maps_collection.find_one({"seed": seed}) is None:
            return {"seed": seed}
