from fastapi import APIRouter
from .db import merchant
from .item_catalog import serialize_shop_item


router = APIRouter(prefix="/database/merchant")


@router.get("/items")
def get_item_shop():
    docs = list(merchant.find({}))
    return {"status": "success", "items": [serialize_shop_item(doc) for doc in docs]}
