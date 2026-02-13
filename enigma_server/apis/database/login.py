from fastapi import APIRouter, HTTPException
from .db import users_collection
import bcrypt

router = APIRouter(prefix="/database/users")

@router.post("/login")
def login_user(username: str, passwd: str):
    # Find user in database
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    # Convert ObjectId to string
    user["_id"] = str(user["_id"])

    # Get hashed password from DB (stored as bytes)
    hashed = user.get("password")
    if not hashed:
        raise HTTPException(status_code=500, detail="User has no password set")

    # bcrypt expects bytes
    passwd_bytes = passwd.encode('utf-8')
    hashed_bytes = hashed if isinstance(hashed, bytes) else hashed.encode('utf-8')

    # Check password
    if not bcrypt.checkpw(passwd_bytes, hashed_bytes):
        raise HTTPException(status_code=401, detail="Incorrect password")

    # Remove password from returned data for security
    user_data = user.copy()
    user_data.pop("password", None)

    return {"status": "success", "user": user_data}
