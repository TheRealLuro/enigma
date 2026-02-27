import requests, os
from dotenv import load_dotenv
from pathlib import Path

env_path = Path(__file__).resolve().parents[2] / ".env"
load_dotenv(dotenv_path=env_path)

api_key = os.getenv("API_TOKEN")

def upload_image(image_b64):
    url = "https://api.imgbb.com/1/upload"

    payload = {
        "key": api_key,
        "image": image_b64
    }

    response = requests.post(url, data=payload)

    if response.status_code == 200:
        data = response.json()
        return data["data"]["url"]
    else:
        print("Upload failed:", response.text)
        return None
    