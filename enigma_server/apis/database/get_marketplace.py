from fastapi import APIRouter, Request

from main import limiter

from .db import maps_collection, marketplace_collection
from .map_utils import serialize_marketplace_listing

router = APIRouter(prefix="/database/marketplace")


@router.get("/listings")
@limiter.limit("30/minute")
def get_marketplace_listings(request: Request):
    listings = list(marketplace_collection.find({}).sort("listed_at", -1))

    map_names = [listing.get("map_name") for listing in listings if listing.get("map_name")]
    map_docs = list(maps_collection.find({"map_name": {"$in": map_names}})) if map_names else []
    map_lookup = {map_doc.get("map_name"): map_doc for map_doc in map_docs}

    return {
        "status": "success",
        "listings": [
            serialize_marketplace_listing(listing, map_lookup.get(listing.get("map_name")))
            for listing in listings
        ],
    }
