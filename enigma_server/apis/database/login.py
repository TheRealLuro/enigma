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


    user["_id"] = str(user["_id"])

    hashed = user.get("password")
    if not hashed:
        raise HTTPException(status_code=500, detail="User has no password set")

    passwd_bytes = passwd.encode('utf-8')
    hashed_bytes = hashed if isinstance(hashed, bytes) else hashed.encode('utf-8')

    if not bcrypt.checkpw(passwd_bytes, hashed_bytes):
        raise HTTPException(status_code=401, detail="Incorrect password")

    user_data = user.copy()
    user_data.pop("password", None)

    return {"status": "success", "user": user_data}
