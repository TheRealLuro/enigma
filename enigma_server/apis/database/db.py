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
app_token = os.getenv("APP_TOKEN")

uri = f"mongodb+srv://{user}:{password}{cluster}"



client = MongoClient(uri)
db = client[db_name]

users_collection = db.users
maps_collection = db.maps
marketplace_collection = db.map_marketplace
merchant = db.item_shop
shop_state = db.shop_state
item_inventory = db.item_inventory
