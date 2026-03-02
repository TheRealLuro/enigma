from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from apis.database.map_images import backfill_map_images, list_maps_missing_images

## THIS IS INCASE IMGBB IS DOWN AND MAPS NEED IMAGES, WORST CASE SCENARIO

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="List maps missing hosted images, or backfill those images once hosting is available."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    list_parser = subparsers.add_parser("list", help="List maps that are missing hosted images.")
    list_parser.add_argument("--limit", type=int, default=None, help="Only inspect the first N maps.")

    backfill_parser = subparsers.add_parser("backfill", help="Generate and upload missing map images.")
    backfill_parser.add_argument("--limit", type=int, default=None, help="Only backfill the first N maps.")
    backfill_parser.add_argument(
        "--no-diffusion",
        action="store_true",
        help="Disable diffusion while generating fallback images.",
    )

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    if args.command == "list":
        maps = list_maps_missing_images(limit=args.limit)
        print(json.dumps({"count": len(maps), "maps": maps}, indent=2))
        return 0

    results = backfill_map_images(limit=args.limit, use_diffusion=not args.no_diffusion)
    status_counts: dict[str, int] = {}
    for result in results:
        status = result.get("status", "unknown")
        status_counts[status] = status_counts.get(status, 0) + 1

    print(
        json.dumps(
            {
                "processed": len(results),
                "status_counts": status_counts,
                "results": results,
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
