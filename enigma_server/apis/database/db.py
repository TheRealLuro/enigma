from dotenv import load_dotenv
import os
from pathlib import Path
from pymongo import MongoClient

env_file = Path(".env")
load_dotenv(dotenv_path=env_file)

user = os.getenv("MONGO_USER")
password = os.getenv("MONGO_PASSWORD")
cluster = os.getenv("MONGO_CLUSTER")
db_name = os.getenv("MONGO_DB")

# Build URI
uri = f"mongodb+srv://{user}:{password}{cluster}"


# Connect
client = MongoClient(uri)
db = client[db_name]

users_collection = db.users
maps_collection = db.maps
map_leaderboards_collection = db.map_leaderboards
game_sessions_collection = db.game_sessions
user_map_progress_collection = db.user_map_progress