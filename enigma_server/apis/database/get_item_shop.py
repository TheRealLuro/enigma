from fastapi import APIRouter

from .db import item_inventory, maps_collection, merchant, users_collection
from .founders_mark import FOUNDERS_MARK_ITEM_ID, evaluate_founders_mark_requirements
from .item_catalog import is_item_supported_for_current_app, serialize_shop_item


router = APIRouter(prefix="/database/merchant")


@router.get("/items")
def get_item_shop(username: str | None = None):
    merchant_docs = list(merchant.find({}))
    permanent_docs = list(
        item_inventory.find(
            {
                "always_available": True,
                "never_restock": True,
                "stock": {"$gt": 0},
                "retired": {"$ne": True},
            }
        )
    )

    by_item_id = {}
    for document in [*merchant_docs, *permanent_docs]:
        item_id = str(document.get("item_id") or "").strip()
        if not item_id:
            continue
        by_item_id[item_id] = document

    user = users_collection.find_one({"username": username}) if username else None
    owned_cosmetics = {
        str(item_id).strip()
        for item_id in (user.get("owned_cosmetics", []) or [])
        if str(item_id).strip()
    } if user else set()
    items = []
    for document in by_item_id.values():
        if not is_item_supported_for_current_app(document):
            continue

        serialized = serialize_shop_item(document)
        item_id = serialized.get("item_id", "")
        if serialized.get("category") == "cosmetic" and item_id in owned_cosmetics:
            continue

        owned_count = 0
        if user:
            owned_count = int((user.get("item_counts", {}) or {}).get(item_id, 0) or 0)
            if serialized.get("category") == "cosmetic" and item_id in owned_cosmetics:
                owned_count = max(owned_count, 1)

        serialized["is_owned"] = owned_count > 0
        serialized["can_purchase"] = serialized["stock"] > 0
        serialized["purchase_blocked_reason"] = None
        serialized["requirements"] = []

        if item_id == FOUNDERS_MARK_ITEM_ID and user:
            eligibility = evaluate_founders_mark_requirements(user, maps_collection)
            serialized["is_owned"] = bool(eligibility["is_owned"])
            serialized["can_purchase"] = bool(eligibility["eligible"]) and serialized["stock"] > 0
            serialized["purchase_blocked_reason"] = eligibility["reason"]
            serialized["requirements"] = eligibility["requirements"]
        elif user and serialized["purchase_limit"] == 1 and owned_count > 0:
            serialized["can_purchase"] = False
            serialized["purchase_blocked_reason"] = "Already owned."

        items.append(serialized)

    items.sort(
        key=lambda item: (
            0 if item.get("always_available") else 1,
            str(item.get("name") or "").lower(),
        )
    )

    return {"status": "success", "items": items}
