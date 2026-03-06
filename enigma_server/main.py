import json

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pymongo.errors import PyMongoError, WaitQueueTimeoutError
from starlette.exceptions import HTTPException as StarletteHTTPException
import asyncio
import logging
import diffusionengine
from fastapi.middleware.cors import CORSMiddleware

from apis.database.input_validation import sanitize_request_string
from apis.database.perf_monitor import request_finished, request_started
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
logger = logging.getLogger("enigma.server")


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

MAX_SCANNED_JSON_BODY_BYTES = 262_144


def _scan_payload_strings(payload, path: str = "body") -> str | None:
    if isinstance(payload, dict):
        for key, value in payload.items():
            key_label = str(key or "").strip() or "field"
            child_path = f"{path}.{key_label}"
            issue = _scan_payload_strings(value, child_path)
            if issue:
                return issue
        return None
    if isinstance(payload, list):
        for index, entry in enumerate(payload):
            issue = _scan_payload_strings(entry, f"{path}[{index}]")
            if issue:
                return issue
        return None
    if isinstance(payload, str):
        issue = sanitize_request_string(payload)
        if issue:
            return f"{path} {issue}"
    return None


@app.middleware("http")
async def request_input_security_middleware(request: Request, call_next):
    for key, value in request.query_params.multi_items():
        issue = sanitize_request_string(value)
        if issue:
            return JSONResponse(
                status_code=400,
                content={"detail": f"Invalid query parameter '{key}': {issue}"},
            )

    content_type = str(request.headers.get("content-type") or "").lower()
    if request.method.upper() in {"POST", "PUT", "PATCH", "DELETE"} and "application/json" in content_type:
        body_bytes = await request.body()

        async def receive():
            return {"type": "http.request", "body": body_bytes, "more_body": False}

        request = Request(request.scope, receive)

        if body_bytes and len(body_bytes) <= MAX_SCANNED_JSON_BODY_BYTES:
            try:
                body_payload = json.loads(body_bytes.decode("utf-8"))
            except (UnicodeDecodeError, ValueError):
                body_payload = None
            if body_payload is not None:
                issue = _scan_payload_strings(body_payload)
                if issue:
                    return JSONResponse(status_code=400, content={"detail": f"Invalid request payload: {issue}"})

    return await call_next(request)


@app.middleware("http")
async def runtime_perf_middleware(request: Request, call_next):
    started_at = request_started()
    status_code = 500
    try:
        response = await call_next(request)
        status_code = int(getattr(response, "status_code", 500) or 500)
        return response
    except Exception:
        status_code = 500
        raise
    finally:
        request_finished(request.url.path, status_code, started_at)

@app.exception_handler(RequestValidationError)
async def validation_json_handler(request: Request, exc: RequestValidationError):
    formatted_errors: list[dict[str, str]] = []
    for error in exc.errors():
        location_parts = [
            str(part)
            for part in list(error.get("loc", []))
            if str(part) not in {"body", "query", "path"}
        ]
        field = ".".join(location_parts) if location_parts else "request"
        message = str(error.get("msg") or "Invalid value")
        error_type = str(error.get("type") or "validation_error")
        formatted_errors.append(
            {
                "field": field,
                "message": message,
                "type": error_type,
            }
        )

    top_errors = formatted_errors[:3]
    summary = "; ".join(f"{entry['field']}: {entry['message']}" for entry in top_errors)
    if len(formatted_errors) > 3:
        summary = f"{summary} (+{len(formatted_errors) - 3} more)"

    return JSONResponse(
        status_code=422,
        content={
            "detail": summary or "Request validation failed",
            "errors": formatted_errors,
        },
    )


@app.exception_handler(StarletteHTTPException)
async def http_json_handler(request: Request, exc: StarletteHTTPException):
    return JSONResponse(status_code=exc.status_code, content={"detail": exc.detail})


@app.exception_handler(WaitQueueTimeoutError)
async def mongo_wait_queue_handler(request: Request, exc: WaitQueueTimeoutError):
    logger.warning("Mongo pool saturation on %s %s: %s", request.method, request.url.path, exc)
    return JSONResponse(status_code=503, content={"detail": "Database busy, retry in a moment"})


@app.exception_handler(PyMongoError)
async def mongo_general_handler(request: Request, exc: PyMongoError):
    logger.warning("Mongo error on %s %s: %s", request.method, request.url.path, exc)
    return JSONResponse(status_code=503, content={"detail": "Database temporarily unavailable"})


@app.exception_handler(Exception)
async def fallback_json_handler(request: Request, exc: Exception):
    logger.exception("Unhandled backend exception on %s %s", request.method, request.url.path)
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
