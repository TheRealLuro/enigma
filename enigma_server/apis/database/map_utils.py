from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Iterable

from bson import ObjectId

THEME_LABEL_RULES = [
    ("Neural Membrane", ("neural", "membrane", "biomech")),
    ("Cartoon", ("cartoon", "whimsical", "toon")),
    ("Dungeon", ("dungeon", "crypt", "castle")),
    ("Sewer", ("sewer", "drain")),
    ("Hedge", ("hedge", "garden", "maze_garden")),
    ("Haunted House", ("haunted", "house", "manor", "ghost")),
]


def normalize_object_id(value: Any) -> ObjectId | None:
    if isinstance(value, ObjectId):
        return value

    if value is None:
        return None

    try:
        return ObjectId(str(value))
    except Exception:
        return None


def normalize_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def average_rating(value: Any) -> float:
    if isinstance(value, list):
        ratings = []
        for item in value:
            try:
                ratings.append(float(item))
            except (TypeError, ValueError):
                continue
    elif isinstance(value, (int, float)):
        ratings = [float(value)]
    elif isinstance(value, str):
        try:
            ratings = [float(value)]
        except ValueError:
            ratings = []
    else:
        ratings = []

    if not ratings:
        return 0.0

    return round(sum(ratings) / len(ratings), 2)


def rating_count(value: Any) -> int:
    if isinstance(value, list):
        count = 0
        for item in value:
            try:
                float(item)
                count += 1
            except (TypeError, ValueError):
                continue
        return count
    if isinstance(value, (int, float)):
        return 1
    if isinstance(value, str):
        try:
            float(value)
            return 1
        except ValueError:
            return 0
    return 0


def time_to_milliseconds(best_time: Any) -> int | None:
    if isinstance(best_time, dict):
        return (
            normalize_int(best_time.get("hours")) * 3_600_000
            + normalize_int(best_time.get("minutes")) * 60_000
            + normalize_int(best_time.get("seconds")) * 1_000
            + normalize_int(best_time.get("milliseconds"))
        )

    if isinstance(best_time, str):
        try:
            hours, minutes, seconds, milliseconds = [int(part) for part in best_time.split(":")]
            return hours * 3_600_000 + minutes * 60_000 + seconds * 1_000 + milliseconds
        except (TypeError, ValueError):
            return None

    return None


def format_best_time(best_time: Any) -> str:
    if isinstance(best_time, dict):
        return (
            f"{normalize_int(best_time.get('hours')):02}:"
            f"{normalize_int(best_time.get('minutes')):02}:"
            f"{normalize_int(best_time.get('seconds')):02}:"
            f"{normalize_int(best_time.get('milliseconds')):03}"
        )

    if isinstance(best_time, str) and best_time.strip():
        return best_time

    return "N/A"


def serialize_datetime(value: Any) -> tuple[str | None, str]:
    if isinstance(value, datetime):
        if value.tzinfo is None:
            value = value.replace(tzinfo=timezone.utc)
        utc_value = value.astimezone(timezone.utc)
        hour_value = utc_value.strftime("%I").lstrip("0") or "12"
        return utc_value.isoformat(), f"{utc_value.strftime('%b %d, %Y')} at {hour_value}:{utc_value.strftime('%M %p UTC')}"

    if isinstance(value, str) and value.strip():
        normalized_value = value.strip()
        try:
            parsed = datetime.fromisoformat(normalized_value.replace("Z", "+00:00"))
            if parsed.tzinfo is None:
                parsed = parsed.replace(tzinfo=timezone.utc)
            utc_value = parsed.astimezone(timezone.utc)
            hour_value = utc_value.strftime("%I").lstrip("0") or "12"
            return utc_value.isoformat(), f"{utc_value.strftime('%b %d, %Y')} at {hour_value}:{utc_value.strftime('%M %p UTC')}"
        except ValueError:
            return normalized_value, normalized_value

    return None, "Unknown"


def normalize_theme_label(value: Any) -> str:
    raw_value = str(value or "").strip().lower().replace("-", "_").replace(" ", "_")
    if not raw_value:
        return "Cartoon"

    for label, keys in THEME_LABEL_RULES:
        if any(key in raw_value for key in keys):
            return label

    return "Cartoon"


def load_maps_by_ids(map_ids: Iterable[Any], collection) -> list[dict]:
    ordered_ids: list[ObjectId] = []
    seen: set[str] = set()

    for raw_id in map_ids:
        object_id = normalize_object_id(raw_id)
        if object_id is None:
            continue

        object_id_key = str(object_id)
        if object_id_key in seen:
            continue

        seen.add(object_id_key)
        ordered_ids.append(object_id)

    if not ordered_ids:
        return []

    documents = list(collection.find({"_id": {"$in": ordered_ids}}))
    document_lookup = {str(document["_id"]): document for document in documents}

    return [document_lookup[str(object_id)] for object_id in ordered_ids if str(object_id) in document_lookup]


def serialize_map_document(map_doc: dict[str, Any]) -> dict[str, Any]:
    founded_at, founded_display = serialize_datetime(map_doc.get("time_founded"))
    ratings = map_doc.get("rating", [])
    map_image = map_doc.get("map_image") or None
    theme_label = normalize_theme_label(map_doc.get("theme"))
    best_time_display = format_best_time(map_doc.get("best_time"))

    return {
        "id": str(map_doc.get("_id", "")),
        "map_name": map_doc.get("map_name", "Unnamed Map"),
        "map_image": map_image,
        "image_available": bool(map_image),
        "image_status": map_doc.get("image_status") or ("ready" if map_image else "pending_upload"),
        "image_upload_error": map_doc.get("image_upload_error"),
        "theme": theme_label,
        "theme_label": theme_label,
        "difficulty": map_doc.get("difficulty", "unknown"),
        "size": normalize_int(map_doc.get("size")),
        "founder": map_doc.get("founder", "Unknown"),
        "owner": map_doc.get("owner", "Unknown"),
        "value": normalize_int(map_doc.get("value")),
        "sold_for_last": normalize_int(map_doc.get("sold_for_last")),
        "plays": normalize_int(map_doc.get("plays")),
        "best_time": best_time_display,
        "best_time_display": best_time_display,
        "best_time_ms": time_to_milliseconds(map_doc.get("best_time")),
        "user_with_best_time": map_doc.get("user_with_best_time", "Unknown"),
        "time_founded": founded_at,
        "time_founded_display": founded_display,
        "founded_display": founded_display,
        "rating_average": average_rating(ratings),
        "rating_count": rating_count(ratings),
    }


def serialize_user_map_document(map_doc: dict[str, Any]) -> dict[str, Any]:
    map_image = map_doc.get("map_image") or None
    theme_label = normalize_theme_label(map_doc.get("theme"))
    best_time_display = format_best_time(map_doc.get("best_time"))

    return {
        "id": str(map_doc.get("_id", "")),
        "map_name": map_doc.get("map_name", "Unnamed Map"),
        "map_image": map_image,
        "image_available": bool(map_image),
        "image_status": map_doc.get("image_status") or ("ready" if map_image else "pending_upload"),
        "theme": theme_label,
        "theme_label": theme_label,
        "difficulty": map_doc.get("difficulty", "unknown"),
        "size": normalize_int(map_doc.get("size")),
        "value": normalize_int(map_doc.get("value")),
        "best_time": best_time_display,
        "best_time_display": best_time_display,
        "best_time_ms": time_to_milliseconds(map_doc.get("best_time")),
        "user_with_best_time": map_doc.get("user_with_best_time", "Unknown"),
        "owner": map_doc.get("owner", "Unknown"),
        "founder": map_doc.get("founder", "Unknown"),
    }


def serialize_map_documents(map_docs: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    serialized: list[dict[str, Any]] = []
    seen: set[str] = set()

    for map_doc in map_docs:
        normalized = serialize_map_document(map_doc)
        map_id = normalized.get("id")
        if not map_id or map_id in seen:
            continue

        seen.add(map_id)
        serialized.append(normalized)

    return serialized


def serialize_user_map_documents(map_docs: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    serialized: list[dict[str, Any]] = []
    seen: set[str] = set()

    for map_doc in map_docs:
        normalized = serialize_user_map_document(map_doc)
        map_id = normalized.get("id")
        if not map_id or map_id in seen:
            continue

        seen.add(map_id)
        serialized.append(normalized)

    return serialized


def serialize_marketplace_listing(listing_doc: dict[str, Any], map_doc: dict[str, Any] | None = None) -> dict[str, Any]:
    listed_at, listed_at_display = serialize_datetime(listing_doc.get("listed_at"))
    last_bought_source = listing_doc.get("last_bought")
    if last_bought_source is None and map_doc is not None:
        last_bought_source = map_doc.get("last_bought")

    last_bought, last_bought_display = serialize_datetime(last_bought_source)
    map_image = listing_doc.get("map_image") or (map_doc.get("map_image") if map_doc else None)
    image_status = listing_doc.get("image_status") or (map_doc.get("image_status") if map_doc else None)
    theme_label = normalize_theme_label(listing_doc.get("theme") or (map_doc.get("theme") if map_doc else None))

    return {
        "id": str(listing_doc.get("_id", "")),
        "map_name": listing_doc.get("map_name", "Unnamed Map"),
        "map_image": map_image,
        "image_available": bool(map_image),
        "image_status": image_status or ("ready" if map_image else "pending_upload"),
        "theme": theme_label,
        "theme_label": theme_label,
        "difficulty": listing_doc.get("difficulty") or (map_doc.get("difficulty") if map_doc else "unknown"),
        "size": normalize_int(listing_doc.get("size") if "size" in listing_doc else (map_doc.get("size") if map_doc else 0)),
        "value": normalize_int(listing_doc.get("value") if "value" in listing_doc else (map_doc.get("value") if map_doc else 0)),
        "price": normalize_int(listing_doc.get("price")),
        "seller": listing_doc.get("seller", "Unknown"),
        "sold_for_last": normalize_int(
            listing_doc.get("sold_for_last")
            if "sold_for_last" in listing_doc
            else (map_doc.get("sold_for_last") if map_doc else 0)
        ),
        "listed_at": listed_at,
        "listed_at_display": listed_at_display,
        "last_bought": last_bought,
        "last_bought_display": last_bought_display,
    }
