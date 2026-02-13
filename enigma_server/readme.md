# install docker desktop

## in cmd run these:
- cd path/to/enigma-server

- docker pull ngrok/ngrok
- docker build -t enigma_server
- docker run -d -p 8000:8000 --name enigma-server enigma-server
- docker run --rm -it -e NGROK_AUTHTOKEN=YOUR_TOKEN ngrok/ngrok:latest http host.docker.internal:8000
