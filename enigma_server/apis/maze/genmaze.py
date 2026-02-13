from fastapi import APIRouter, HTTPException
from apis.maze.maze import get_seed

router = APIRouter(prefix="/maze")


@router.get("/genseed")
def return_seed(difficulty: str, size: int):

    if size <= 1:
        raise HTTPException(status_code=400, detail="Invalid size must be greater than 1")

    raw_seed = get_seed(size)
    seed = f'{difficulty}-{raw_seed}'

    return {
        "seed": seed
    }
