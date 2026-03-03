from pymongo.errors import DuplicateKeyError

from .db import item_inventory
from .item_catalog import DEFAULT_ITEM_CATALOG, normalize_item_doc


ITEMS = [normalize_item_doc(item) for item in DEFAULT_ITEM_CATALOG]


def seed_item_inventory():
    item_inventory.create_index("item_id", unique=True)
    inserted = 0
    for item in ITEMS:
        try:
            item_inventory.insert_one(item)
            inserted += 1
        except DuplicateKeyError:
            continue
    return inserted


if __name__ == "__main__":
    count = seed_item_inventory()
    print(f"Inserted {count} item(s) into item_inventory.")
