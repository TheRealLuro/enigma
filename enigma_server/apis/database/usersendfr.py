from fastapi import APIRouter, HTTPException, Request
from .db import users_collection
from main import limiter

router = APIRouter(prefix="/database/users")


@router.post("/send_fr")
@limiter.limit("10/minute")
def send_request(request: Request, sender_user: str, receiver_user: str):

    

    r_user = users_collection.find_one({"username": receiver_user})

    if sender_user == receiver_user:
        raise HTTPException(status_code=400, detail="You cannot add yourself")

    if not r_user:
        raise HTTPException(status_code=404, detail="User not found")
    
    if sender_user in r_user['friend_requests']:
         raise HTTPException(status_code=400, detail="User already added")

    
    # just adds their username to the requests on the other persons profile
    update_query = {"$addToSet": {"friend_requests": sender_user}}

    users_collection.update_one({"username": receiver_user}, update_query)

    return {"status": "Friend request sent!"}

