from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from apis.database.account_cleanup import delete_user_account
from apis.database.db import users_collection


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Delete accounts inactive for 60+ consecutive days.")
    parser.add_argument("--days", type=int, default=60, help="Inactive-day threshold. Defaults to 60.")
    parser.add_argument("--dry-run", action="store_true", help="List accounts without deleting them.")
    return parser


def parse_login(value) -> datetime | None:
    if isinstance(value, datetime):
        return value if value.tzinfo else value.replace(tzinfo=timezone.utc)
    if isinstance(value, str):
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
            return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
        except ValueError:
            return None
    return None


def main() -> int:
    args = build_parser().parse_args()
    cutoff = datetime.now(timezone.utc) - timedelta(days=args.days)
    deleted: list[str] = []
    candidates: list[str] = []

    for user in users_collection.find({"is_system_account": {"$ne": True}}):
        last_login = parse_login(user.get("last_login_at"))
        if last_login is None or last_login > cutoff:
            continue
        username = user.get("username")
        if not username:
            continue
        candidates.append(username)
        if not args.dry_run:
            delete_user_account(username)
            deleted.append(username)

    print(
        json.dumps(
            {
                "threshold_days": args.days,
                "cutoff_utc": cutoff.isoformat(),
                "candidates": candidates,
                "deleted": deleted,
                "dry_run": args.dry_run,
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
