from fastapi import APIRouter, HTTPException, Request
from .db import users_collection, app_token
import bcrypt
from main import limiter
from decoder import decode

router = APIRouter(prefix="/database/users")

@router.post("/login")
@limiter.limit("2/minute")
def login_user(request: Request, username: str, passwd: str, token: str):

    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)
    
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")


    hashed = user.get("password")
    passwd_bytes = passwd.encode('utf-8')
    hashed_bytes = hashed if isinstance(hashed, bytes) else hashed.encode('utf-8')

    if not bcrypt.checkpw(passwd_bytes, hashed_bytes):
        raise HTTPException(status_code=401, detail="Incorrect password")
    
    from datetime import datetime, timezone

    DAILY_REWARD = 50
    now = datetime.now(timezone.utc)
    last_login_at = user.get("last_login_at")

    update = {"$set": {"last_login_at": now}}
    rewarded = False

    if last_login_at is None or last_login_at.date() < now.date():
        update["$inc"] = {"maze_nuggets": DAILY_REWARD}
        rewarded = True

    users_collection.update_one({"username": username}, update)

    user = users_collection.find_one({"username": username})


    user_data = user.copy()
    
    user_data.pop("_id", None)
    user_data.pop("password", None)


    return {
    "status": "success",
    "user": user_data,
    "daily_reward_granted": rewarded,
    "daily_reward_amount": DAILY_REWARD if rewarded else 0
    }

