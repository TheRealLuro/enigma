from fastapi import APIRouter, HTTPException, Request
from .db import users_collection, app_token, maps_collection
import bcrypt
from main import limiter
from decoder import decode
from bson import ObjectId

router = APIRouter(prefix="/database/users")

@router.get("/getuser")
@limiter.limit("10/minute")
def get_user(request: Request, username: str, passwd: str, token: str):

    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(401)
    
    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")


    user["_id"] = str(user["_id"])

    hashed = user.get("password")
    passwd_bytes = passwd.encode('utf-8')
    hashed_bytes = hashed if isinstance(hashed, bytes) else hashed.encode('utf-8')

    if not bcrypt.checkpw(passwd_bytes, hashed_bytes):
        raise HTTPException(status_code=401, detail="Incorrect password")
    

    user_maps = user.get("maps_discovered", [])

    maps = []
    for map in user_maps:
        map_doc = maps_collection.find_one({"_id": ObjectId(map)})
        if map_doc:
            map_doc.pop("_id", None)
            maps.append(map_doc)

    user["maps_discovered"] = maps

    user_data = user.copy()
    user_data.pop("password", None)




    return {"status": "success", "user": user_data}
