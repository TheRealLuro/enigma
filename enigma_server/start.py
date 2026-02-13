import subprocess
import time
from pyngrok import ngrok, conf
from dotenv import load_dotenv
import os
from pathlib import Path

env_file = Path(".env")
load_dotenv(dotenv_path=env_file)

# Set ngrok auth token
conf.get_default().auth_token = os.getenv("NGROK_KEY")

# Start FastAPI in a subprocess
uvicorn_process = subprocess.Popen(
    ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
)

# Wait a moment for the server to start
time.sleep(3)

# Start ngrok tunnel
url = ngrok.connect(8000)
print(f"🌍 PUBLIC URL: {url}")

# Keep the script alive
try:
    while True:
        time.sleep(60)
except KeyboardInterrupt:
    uvicorn_process.terminate()
    print("Stopped FastAPI and ngrok")
