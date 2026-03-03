from __future__ import annotations

import json
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from apis.database.db import item_inventory, merchant
from apis.database.item_catalog import DEFAULT_ITEM_CATALOG, normalize_item_doc
from apis.database.item_shop_stocker import restock_item_shop


def main() -> int:
    desired = {item["item_id"]: normalize_item_doc(item) for item in DEFAULT_ITEM_CATALOG}
    active_ids = set(desired)

    item_inventory.create_index("item_id", unique=True)
    upserts = 0
    retired = 0

    for item_id, item in desired.items():
        existing = item_inventory.find_one({"item_id": item_id}, {"stock": 1, "never_restock": 1})
        update_doc = {**item, "retired": False}
        if existing and bool(existing.get("never_restock")) and item.get("never_restock"):
            update_doc["stock"] = int(existing.get("stock", item.get("stock", 0)) or 0)

        item_inventory.update_one({"item_id": item_id}, {"$set": update_doc}, upsert=True)
        upserts += 1

    for existing in item_inventory.find({"item_id": {"$nin": list(active_ids)}}):
        item_inventory.update_one({"_id": existing["_id"]}, {"$set": {"retired": True, "stock": 0}})
        retired += 1

    merchant.delete_many({})
    stocked = restock_item_shop()

    print(json.dumps({"upserts": upserts, "retired": retired, "stocked": len(stocked)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
