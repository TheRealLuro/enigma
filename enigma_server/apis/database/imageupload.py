import os
from pathlib import Path

import requests
from dotenv import load_dotenv

env_path = Path(__file__).resolve().parents[2] / ".env"
load_dotenv(dotenv_path=env_path)

api_key = os.getenv("API_TOKEN")


def upload_image(image_b64):
    if not api_key:
        print("Upload failed: API_TOKEN is not configured.")
        return None

    url = "https://api.imgbb.com/1/upload"
    payload = {
        "key": api_key,
        "image": image_b64,
    }

    try:
        response = requests.post(url, data=payload, timeout=30)
    except requests.RequestException as exc:
        print(f"Upload failed: {exc}")
        return None

    if response.status_code == 200:
        data = response.json()
        return data["data"]["url"]

    failure_excerpt = (response.text or "").strip().replace("\n", " ")[:500]
    print(f"Upload failed: {failure_excerpt}")
    return None
