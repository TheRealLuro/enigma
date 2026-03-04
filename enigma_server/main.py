from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException
import asyncio
import diffusionengine
from fastapi.middleware.cors import CORSMiddleware

from apis.database.item_shop_stocker import ensure_shop_seeded, shop_restock_scheduler
from apis.database.system_accounts import ensure_bank_account

class NoOpLimiter:
    @staticmethod
    def limit(_rule: str):
        def decorator(func):
            return func

        return decorator

limiter = NoOpLimiter()
app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None, default_response_class=JSONResponse)


app.state.limiter = limiter
origins = [
    "https://pro150enigma-dfd8aqh7g4cuaxee.canadacentral-01.azurewebsites.net",
    "http://localhost:5241"
]

app.add_middleware(
    CORSMiddleware,
    allow_origins=origins,  # or ["*"] for testing
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.exception_handler(RequestValidationError)
async def validation_json_handler(request: Request, exc: RequestValidationError):
    return JSONResponse(status_code=422, content={"detail": exc.errors()})


@app.exception_handler(StarletteHTTPException)
async def http_json_handler(request: Request, exc: StarletteHTTPException):
    return JSONResponse(status_code=exc.status_code, content={"detail": exc.detail})


@app.exception_handler(Exception)
async def fallback_json_handler(request: Request, exc: Exception):
    return JSONResponse(status_code=500, content={"detail": "Internal server error"})


import importlib
import pkgutil
import apis

def load_routers(package):
    for _, module_name, _ in pkgutil.walk_packages(
        package.__path__,
        package.__name__ + "."
    ):
        module = importlib.import_module(module_name)
        if hasattr(module, "router"):
            app.include_router(module.router)

load_routers(apis)


@app.on_event("startup")
async def startup_jobs():
    diffusionengine.preload_pipe()
    ensure_bank_account()
    ensure_shop_seeded()
    app.state.shop_restock_task = asyncio.create_task(shop_restock_scheduler())


@app.on_event("shutdown")
async def shutdown_jobs():
    task = getattr(app.state, "shop_restock_task", None)
    if task:
        task.cancel()
