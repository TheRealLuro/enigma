from __future__ import annotations

import time
from datetime import datetime, timezone
from typing import Any

from fastapi import APIRouter, Request

from main import limiter

from .db import client
from .perf_monitor import observe_dependency_latency, snapshot
from .redis_store import get_redis_client

router = APIRouter(prefix="/database/system")


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _to_float(value: Any, default: float = 0.0) -> float:
    try:
        parsed = float(value)
    except (TypeError, ValueError):
        return default
    if parsed != parsed:
        return default
    return parsed


def _measure_redis_latency_ms() -> dict[str, Any]:
    started_at = time.perf_counter()
    try:
        redis_client = get_redis_client()
        redis_client.ping()
        latency_ms = (time.perf_counter() - started_at) * 1000.0
        observe_dependency_latency("redis", latency_ms, ok=True)
        return {"available": True, "latency_ms": round(latency_ms, 3), "error": None}
    except Exception as exc:
        latency_ms = (time.perf_counter() - started_at) * 1000.0
        observe_dependency_latency("redis", latency_ms, ok=False)
        return {"available": False, "latency_ms": round(latency_ms, 3), "error": str(exc)}


def _measure_mongo_wait_estimate_ms() -> dict[str, Any]:
    started_at = time.perf_counter()
    try:
        client.admin.command("ping")
        latency_ms = (time.perf_counter() - started_at) * 1000.0
        observe_dependency_latency("mongo", latency_ms, ok=True)
        return {"available": True, "wait_estimate_ms": round(latency_ms, 3), "error": None}
    except Exception as exc:
        latency_ms = (time.perf_counter() - started_at) * 1000.0
        observe_dependency_latency("mongo", latency_ms, ok=False)
        return {"available": False, "wait_estimate_ms": round(latency_ms, 3), "error": str(exc)}


@router.get("/perf")
@limiter.limit("120/minute")
def get_runtime_perf(request: Request):
    runtime_snapshot = snapshot()
    redis_probe = _measure_redis_latency_ms()
    mongo_probe = _measure_mongo_wait_estimate_ms()

    dependencies = runtime_snapshot.get("dependencies", {})
    redis_stats = dependencies.get("redis", {}) if isinstance(dependencies, dict) else {}
    mongo_stats = dependencies.get("mongo", {}) if isinstance(dependencies, dict) else {}

    return {
        "status": "success",
        "generated_at": _utc_now_iso(),
        "runtime": {
            "uptime_seconds": _to_float(runtime_snapshot.get("uptime_seconds", 0.0)),
            "inflight_requests": int(runtime_snapshot.get("inflight_requests", 0) or 0),
            "peak_inflight_requests": int(runtime_snapshot.get("peak_inflight_requests", 0) or 0),
            "total_requests": int(runtime_snapshot.get("total_requests", 0) or 0),
            "total_errors": int(runtime_snapshot.get("total_errors", 0) or 0),
            "error_rate_percent": _to_float(runtime_snapshot.get("error_rate_percent", 0.0)),
            "rps_10s": _to_float(runtime_snapshot.get("rps_10s", 0.0)),
            "rps_60s": _to_float(runtime_snapshot.get("rps_60s", 0.0)),
            "error_rps_60s": _to_float(runtime_snapshot.get("error_rps_60s", 0.0)),
            "avg_response_ms": _to_float(runtime_snapshot.get("avg_response_ms", 0.0)),
            "p95_response_ms": _to_float(runtime_snapshot.get("p95_response_ms", 0.0)),
            "routes": list(runtime_snapshot.get("routes", []) or []),
        },
        "dependencies": {
            "redis": {
                **redis_probe,
                "avg_latency_ms_60s": _to_float(redis_stats.get("avg_latency_ms_60s", 0.0)),
                "p95_latency_ms_60s": _to_float(redis_stats.get("p95_latency_ms_60s", 0.0)),
                "failure_rate_60s": _to_float(redis_stats.get("failure_rate_60s", 0.0)),
                "samples_60s": int(redis_stats.get("samples_60s", 0) or 0),
            },
            "mongo": {
                **mongo_probe,
                "avg_wait_estimate_ms_60s": _to_float(mongo_stats.get("avg_latency_ms_60s", 0.0)),
                "p95_wait_estimate_ms_60s": _to_float(mongo_stats.get("p95_latency_ms_60s", 0.0)),
                "failure_rate_60s": _to_float(mongo_stats.get("failure_rate_60s", 0.0)),
                "samples_60s": int(mongo_stats.get("samples_60s", 0) or 0),
            },
        },
    }
