from fastapi import APIRouter

from .db import merchant


router = APIRouter(prefix="/database/merchant")


@router.get("/items")
def get_item_shop():
    docs = list(merchant.find({}))
    for doc in docs:
        doc["_id"] = str(doc["_id"])
    return {"items": docs}
