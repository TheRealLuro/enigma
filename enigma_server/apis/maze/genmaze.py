from fastapi import APIRouter, HTTPException
from apis.maze.maze import get_seed
from database.db import app_token
from slowapi import limiter


router = APIRouter(prefix="/maze")


@router.get("/genseed")
@limiter.limit("1/minute")
def return_seed(difficulty: str, size: int, token: str):

    if token != app_token:
        raise HTTPException(status_code=401, detail="You are not allowed, bye.")

    if size <= 1:
        raise HTTPException(status_code=400, detail="Invalid size must be greater than 1")

    raw_seed = get_seed(size)
    seed = f'{difficulty}-{raw_seed}'

    return {
        "seed": seed
    }
