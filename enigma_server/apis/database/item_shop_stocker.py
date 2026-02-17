import asyncio
import random
from datetime import datetime, timedelta, timezone

from pymongo.errors import DuplicateKeyError

from .db import item_inventory, merchant, shop_state


RESTOCK_HOUR_UTC = 16
DAILY_SHOP_SIZE = 4
WEEKLY_REFRESH_STATE_ID = "weekly_inventory_refresh"


def _weekly_stock_range_for_rarity(rarity):
    rarity_key = str(rarity).lower()
    if rarity_key == "common":
        return (15, 40)
    if rarity_key == "rare":
        return (6, 20)
    if rarity_key == "epic":
        return (2, 10)
    if rarity_key in ("legendary", "mythical"):
        return (1, 4)
    return (5, 15)


def ensure_inventory_ready():
    item_inventory.create_index("item_id", unique=True)


def _refresh_weekly_inventory_stock_if_needed(now=None):
    if now is None:
        now = datetime.now(timezone.utc)

    if now.weekday() != 0:  # Monday is 0
        return

    iso = now.isocalendar()
    week_key = f"{iso.year}-W{iso.week:02d}"
    refreshed = False

    update_result = shop_state.update_one(
        {"_id": WEEKLY_REFRESH_STATE_ID, "week_key": {"$ne": week_key}},
        {"$set": {"week_key": week_key, "refreshed_at": now}},
    )
    if update_result.matched_count == 1:
        refreshed = True
    elif shop_state.count_documents({"_id": WEEKLY_REFRESH_STATE_ID}, limit=1) == 0:
        try:
            shop_state.insert_one(
                {"_id": WEEKLY_REFRESH_STATE_ID, "week_key": week_key, "refreshed_at": now}
            )
            refreshed = True
        except DuplicateKeyError:
            refreshed = False

    if not refreshed:
        return

    for item in item_inventory.find({}, {"_id": 1, "rarity": 1}):
        min_stock, max_stock = _weekly_stock_range_for_rarity(item.get("rarity"))
        item_inventory.update_one(
            {"_id": item["_id"]},
            {"$set": {"stock": random.randint(min_stock, max_stock)}},
        )


def _next_restock_at_utc(now=None):
    if now is None:
        now = datetime.now(timezone.utc)
    restock_at = now.replace(hour=RESTOCK_HOUR_UTC, minute=0, second=0, microsecond=0)
    if now >= restock_at:
        restock_at = restock_at + timedelta(days=1)
    return restock_at


def restock_item_shop():
    now = datetime.now(timezone.utc)
    ensure_inventory_ready()
    _refresh_weekly_inventory_stock_if_needed(now)

    available_items = list(item_inventory.find({"stock": {"$gt": 0}}))
    if len(available_items) < DAILY_SHOP_SIZE:
        raise ValueError("Not enough in-stock items in item_inventory to fill the daily shop")

    chosen_items = random.sample(available_items, DAILY_SHOP_SIZE)
    docs = []
    for item in chosen_items:
        item.pop("_id", None)
        item["stocked_at"] = now
        docs.append(item)

    merchant.delete_many({})
    merchant.insert_many(docs)
    return docs


def ensure_shop_seeded():
    ensure_inventory_ready()
    if merchant.count_documents({}) == 0:
        restock_item_shop()


async def shop_restock_scheduler():
    while True:
        now = datetime.now(timezone.utc)
        next_restock = _next_restock_at_utc(now)
        wait_seconds = max(1, int((next_restock - now).total_seconds()))
        await asyncio.sleep(wait_seconds)
        restock_item_shop()
