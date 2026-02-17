from fastapi import APIRouter, HTTPException, Request
from .db import users_collection, app_token
import bcrypt
from main import limiter
from decoder import decode
from datetime import datetime, timezone

router = APIRouter(prefix="/database/users")

@router.post("/signup")
@limiter.limit("2/minute")
def create_user(request: Request, username: str, email: str, passwd: str, token: str):

    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)
    
    if users_collection.find_one({"username": username}):
        raise HTTPException(status_code=400, detail="User already exists")
    
    password_bytes = passwd.encode('utf-8')
    hashed_bytes = bcrypt.hashpw(password_bytes, bcrypt.gensalt())
    hashed_password = hashed_bytes.decode('utf-8')  


    user = {
        "username": username,
        "email": email,
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
        "last_login_at": datetime.now(timezone.utc)
    }
    
    result = users_collection.insert_one(user)
    return {"status": "success", "user_id": str(result.inserted_id)}
