# install docker desktop

## in cmd run these:
- cd path/to/enigma_server

- docker pull ngrok/ngrok
- docker network create enigma-network
- docker run -d -p 6379:6379 --name redis --network enigma-network redis:7.4-alpine redis-server --appendonly yes
- docker build -t enigma_server .
- docker run -d -p 8000:8000 --env-file .env --network enigma-network --name enigma-server enigma_server
- docker run --rm -it -e NGROK_AUTHTOKEN=YOUR_TOKEN ngrok/ngrok:latest http host.docker.internal:8000

## app connection:
- keep `REDIS_URL=redis://redis:6379/0` in `.env`
- the app does not call redis directly
- set `ENIGMA_BACKEND_URL` to:
  - `https://nonelastic-prorailroad-gillian.ngrok-free.dev/`

## notes:
- redis and the backend are on the same docker network in this setup, so `redis://redis:6379/0` is the correct redis url.
- only the backend container should talk to redis.
