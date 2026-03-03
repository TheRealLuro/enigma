# install docker desktop

## in cmd run these- cd path/to/enigma_server

- docker pull ngrok/ngrok
- docker network create enigma-network
- docker run -d -p 6379:6379 --name redis --network enigma-network redis:7.4-alpine redis-server --appendonly yes
- docker build -t enigma_server .
- docker run -d -p 8000:8000 --env-file .env --network enigma-network --name enigma-server enigma_server
- docker run --rm -it -e NGROK_AUTHTOKEN=YO39bEw4Gb2DVmOH6G4afSToQ08zl_3gw3PYRtddxMgtE3Vtasgrok/ngrok:latest http host.docker.internal:8000

#### app uses the public ngrok url:
- set `ENIGMA_BACKEND_URL` to:
  - `https://nonelastic-prorailroad-gillian.ngrok-free.dev/`l:8000
