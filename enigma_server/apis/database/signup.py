from fastapi import APIRouter, HTTPException
from .db import users_collection
import bcrypt

router = APIRouter(prefix="/database/users")

@router.post("/new")
def create_user(username: str, passwd: str):
    if users_collection.find_one({"username": username}):
        raise HTTPException(status_code=400, detail="User already exists")
    
    password_bytes = passwd.encode('utf-8')
    hashed_bytes = bcrypt.hashpw(password_bytes, bcrypt.gensalt())
    hashed_password = hashed_bytes.decode('utf-8')  

    maps_discovered = []
    number_of_maps_played = 0
    maps_completed = 0
    maps_lost = 0

    user = {
        "username": username,
        "password": hashed_password,
        "maps_discovered":maps_discovered,
        "number_of_maps_played": number_of_maps_played,
        "maps_completed": maps_completed,
        "maps_lost": maps_lost
    }
    
    result = users_collection.insert_one(user)
    return {"status": "success", "user_id": str(result.inserted_id)}
