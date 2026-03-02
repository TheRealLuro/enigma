from __future__ import annotations

from typing import Any

from imagegen import generate_map_image_payload

from .db import maps_collection
from .imageupload import upload_image
from .map_utils import serialize_map_documents

MISSING_IMAGE_QUERY = {
    "$or": [
        {"map_image": {"$exists": False}},
        {"map_image": None},
        {"map_image": ""},
        {"image_status": {"$ne": "ready"}},
    ]
}


def get_maps_missing_images(limit: int | None = None) -> list[dict[str, Any]]:
    cursor = maps_collection.find(MISSING_IMAGE_QUERY).sort("time_founded", 1)
    if limit and limit > 0:
        cursor = cursor.limit(limit)
    return list(cursor)


def list_maps_missing_images(limit: int | None = None) -> list[dict[str, Any]]:
    return serialize_map_documents(get_maps_missing_images(limit))


def backfill_map_images(limit: int | None = None, use_diffusion: bool = True) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []

    for map_doc in get_maps_missing_images(limit):
        map_id = str(map_doc.get("_id", ""))
        map_name = map_doc.get("map_name", "Unnamed Map")
        current_theme = map_doc.get("theme") or "Unknown theme"

        try:
            payload = generate_map_image_payload(map_doc["seed"], use_diffusion=use_diffusion)
            current_theme = payload.get("theme") or current_theme
        except RuntimeError as exc:
            maps_collection.update_one(
                {"_id": map_doc["_id"]},
                {
                    "$set": {
                        "theme": current_theme,
                        "image_status": "generation_failed",
                        "image_upload_error": str(exc),
                    }
                },
            )
            results.append(
                {
                    "map_id": map_id,
                    "map_name": map_name,
                    "status": "generation_failed",
                    "detail": str(exc),
                }
            )
            continue

        image_url = upload_image(payload["map_image"])
        if not image_url:
            maps_collection.update_one(
                {"_id": map_doc["_id"]},
                {
                    "$set": {
                        "theme": current_theme,
                        "image_status": "pending_upload",
                        "image_upload_error": "Image host unavailable.",
                    }
                },
            )
            results.append(
                {
                    "map_id": map_id,
                    "map_name": map_name,
                    "status": "pending_upload",
                    "detail": "Image host unavailable.",
                }
            )
            continue

        maps_collection.update_one(
            {"_id": map_doc["_id"]},
            {
                "$set": {
                    "map_image": image_url,
                    "theme": current_theme,
                    "image_status": "ready",
                },
                "$unset": {"image_upload_error": ""},
            },
        )
        results.append(
            {
                "map_id": map_id,
                "map_name": map_name,
                "status": "ready",
                "map_image": image_url,
            }
        )

    return results
