from fastapi import APIRouter, HTTPException, Request


router = APIRouter(prefix="")

@router.get("/what-is-my-ip")
def what_is_my_ip(request: Request):
    forwarded = request.headers.get("x-forwarded-for")

    if forwarded:
        real_ip = forwarded.split(",")[0].strip()
    else:
        real_ip = request.client.host

    return {
        "real_ip": real_ip,
        "client_host": request.client.host
    }
