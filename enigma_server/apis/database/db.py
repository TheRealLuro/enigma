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

uri = f"mongodb+srv://{user}:{password}{cluster}"


mongo_max_pool_size = int(os.getenv("MONGO_MAX_POOL_SIZE", "500") or 500)
mongo_min_pool_size = int(os.getenv("MONGO_MIN_POOL_SIZE", "10") or 10)
mongo_wait_queue_timeout_ms = int(os.getenv("MONGO_WAIT_QUEUE_TIMEOUT_MS", "10000") or 10000)
mongo_server_selection_timeout_ms = int(os.getenv("MONGO_SERVER_SELECTION_TIMEOUT_MS", "7000") or 7000)
mongo_connect_timeout_ms = int(os.getenv("MONGO_CONNECT_TIMEOUT_MS", "7000") or 7000)
mongo_socket_timeout_ms = int(os.getenv("MONGO_SOCKET_TIMEOUT_MS", "20000") or 20000)
mongo_max_connecting = int(os.getenv("MONGO_MAX_CONNECTING", "32") or 32)

client = MongoClient(
    uri,
    maxPoolSize=max(50, mongo_max_pool_size),
    minPoolSize=max(0, mongo_min_pool_size),
    waitQueueTimeoutMS=max(500, mongo_wait_queue_timeout_ms),
    serverSelectionTimeoutMS=max(1000, mongo_server_selection_timeout_ms),
    connectTimeoutMS=max(1000, mongo_connect_timeout_ms),
    socketTimeoutMS=max(1000, mongo_socket_timeout_ms),
    maxConnecting=max(2, mongo_max_connecting),
    retryWrites=True,
)
db = client[db_name]

users_collection = db.users
maps_collection = db.maps
marketplace_collection = db.map_marketplace
merchant = db.item_shop
shop_state = db.shop_state
item_inventory = db.item_inventory
run_results = db.run_results
governance_sessions = db.governance_sessions
governance_votes = db.governance_votes
