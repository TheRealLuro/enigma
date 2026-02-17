from fastapi import APIRouter, HTTPException, Request
from .db import users_collection, app_token
from main import limiter
from decoder import decode

router = APIRouter(prefix="/database/users")


@router.post("/accept_fr")
@limiter.limit("10/minute")
def accept_request(request: Request, username: str, adding: str, token: str):

    import hmac
    if not hmac.compare_digest(decode(token), app_token):
        raise HTTPException(status_code=401, detail="Invalid token")
     

    user = users_collection.find_one({"username": username})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    adding_user = users_collection.find_one({"username": adding})
    if not adding_user:
        raise HTTPException(status_code=404, detail="Requested user not found")

    if adding == username:
        raise HTTPException(status_code=400, detail="You cannot add yourself")

    if adding in user["friends"]:
        raise HTTPException(status_code=409, detail="Users are already friends")

    if adding not in user["friend_requests"]:
        raise HTTPException(status_code=404, detail="Friend request not found")
    

    update_query = {"$addToSet": {"friends": adding}, "$pull": {"friend_requests": adding}}
    update_query_r = {"$addToSet": {"friends": username}, "$pull": {"friend_requests": username}}

    users_collection.update_one({"username": username}, update_query)
    users_collection.update_one({"username": adding}, update_query_r)


    return {"status": "Friend request accepted!"}
