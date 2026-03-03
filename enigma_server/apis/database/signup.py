from datetime import datetime, timezone

import bcrypt
from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel

from main import limiter

from .db import users_collection
from .user_utils import SYSTEM_BANK_USERNAME, default_user_fields, normalize_email

router = APIRouter(prefix="/database/users")


class SignUpPayload(BaseModel):
    username: str
    email: str
    passwd: str


@router.post("/signup")
@limiter.limit("2/minute")
def create_user(request: Request, username: str | None = None, email: str | None = None, passwd: str | None = None, body: SignUpPayload | None = None):
    username = body.username if body else username
    email = body.email if body else email
    passwd = body.passwd if body else passwd

    username = (username or "").strip()
    email = (email or "").strip()
    passwd = passwd or ""

    if not username or not email or not passwd:
        raise HTTPException(status_code=400, detail="Username, email, and password are required")

    if username.lower() == SYSTEM_BANK_USERNAME:
        raise HTTPException(status_code=403, detail="That username is reserved")

    if users_collection.find_one({"username": username}):
        raise HTTPException(status_code=400, detail="User already exists")

    normalized_email = normalize_email(email)
    if users_collection.find_one({"email_normalized": normalized_email}):
        raise HTTPException(status_code=400, detail="Email already exists")
    
    password_bytes = passwd.encode('utf-8')
    hashed_bytes = bcrypt.hashpw(password_bytes, bcrypt.gensalt())
    hashed_password = hashed_bytes.decode('utf-8')  

    defaults = default_user_fields(username, email)

    user = {
        "username": username,
        "email": email,
        "email_normalized": normalized_email,
        "password": hashed_password,
        "maze_nuggets": 0,
        "friends" : [],
        "friend_requests" : [],
        "maps_discovered": [],
        "maps_owned" : [],
        "owned_cosmetics": [],
        "item_counts": {},
        "number_of_maps_played": 0,
        "maps_completed": 0,
        "maps_lost": 0,
        "last_login_at": datetime.now(timezone.utc),
        "profile_image": defaults["profile_image"],
        "tutorial_state": defaults["tutorial_state"],
        "is_system_account": defaults["is_system_account"],
        "allow_public_profile": defaults["allow_public_profile"],
    }
    
    result = users_collection.insert_one(user)
    return {"status": "success", "user_id": str(result.inserted_id)}
