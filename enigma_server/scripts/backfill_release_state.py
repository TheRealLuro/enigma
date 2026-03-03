from __future__ import annotations

import json
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from apis.database.db import maps_collection, marketplace_collection, users_collection
from apis.database.system_accounts import ensure_bank_account
from apis.database.user_utils import build_discovered_to_owned_sync_update, build_user_defaults_update


def ensure_indexes() -> None:
    users_collection.create_index("username", unique=True)
    users_collection.create_index("email_normalized", unique=True, sparse=True)
    users_collection.create_index("last_login_at")
    users_collection.create_index("is_system_account")
    maps_collection.create_index("owner")
    maps_collection.create_index("seed")
    marketplace_collection.create_index("map_name")


def backfill_users() -> dict[str, int]:
    metrics = {"updated_users": 0, "synced_owned_maps": 0}
    for user in users_collection.find({}):
        set_updates = build_user_defaults_update(user)
        add_owned = build_discovered_to_owned_sync_update(user)
        update_query: dict = {}
        if set_updates:
            update_query["$set"] = set_updates
        if add_owned:
            update_query["$addToSet"] = {"maps_owned": {"$each": add_owned}}

        if update_query:
            users_collection.update_one({"_id": user["_id"]}, update_query)
            metrics["updated_users"] += 1
            metrics["synced_owned_maps"] += len(add_owned)
    return metrics


def backfill_maps() -> dict[str, int]:
    metrics = {"updated_maps": 0}
    for map_doc in maps_collection.find({}):
        owner = map_doc.get("owner") or map_doc.get("founder")
        if not owner:
            continue
        result = maps_collection.update_one(
            {"_id": map_doc["_id"], "owner": {"$exists": False}},
            {"$set": {"owner": owner}},
        )
        metrics["updated_maps"] += int(result.modified_count)
    return metrics


def main() -> int:
    ensure_indexes()
    ensure_bank_account()
    summary = {
        "users": backfill_users(),
        "maps": backfill_maps(),
    }
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
