from fastapi import APIRouter, HTTPException, Request
from fastapi.openapi.docs import get_swagger_ui_html
from fastapi.openapi.utils import get_openapi
from main import app


router = APIRouter(prefix="")

import os

ALLOWED_IPS = set(os.getenv("ALLOWED_IPS").split(","))   

def verify_ip(request: Request):

    forwarded = request.headers.get("x-forwarded-for")

    if forwarded:
        client_ip = forwarded.split(",")[0].strip()
    else:
        client_ip = request.client.host

    if client_ip not in ALLOWED_IPS:
        raise HTTPException(403, "Forbidden IP")



@router.get("/docs")
def protected_docs(request: Request):
    verify_ip(request) 
    return get_swagger_ui_html(
        openapi_url="/openapi.json",
        title="Secure Docs"
    )

@router.get("/openapi.json")
def openapi_schema():

    return get_openapi(
        title="Secure API",
        version="1.0.0",
        routes=app.routes
    )




