from __future__ import annotations

import statistics
import threading
import time
from collections import deque
from typing import Any

WINDOW_SECONDS = 60.0
SHORT_WINDOW_SECONDS = 10.0
MAX_LATENCY_SAMPLES = 6000
MAX_ROUTE_LATENCY_SAMPLES = 800
MAX_ROUTE_RECENT_SAMPLES = 1200
MAX_TOP_ROUTES = 12

_lock = threading.Lock()
_started_at = time.monotonic()
_inflight_requests = 0
_peak_inflight_requests = 0
_total_requests = 0
_total_errors = 0
_total_duration_ms = 0.0
_recent_request_timestamps: deque[float] = deque()
_recent_error_timestamps: deque[float] = deque()
_recent_latency_ms: deque[float] = deque(maxlen=MAX_LATENCY_SAMPLES)
_routes: dict[str, dict[str, Any]] = {}
_dependencies: dict[str, dict[str, Any]] = {}


def _prune_locked(now: float) -> None:
    cutoff_60 = now - WINDOW_SECONDS
    while _recent_request_timestamps and _recent_request_timestamps[0] < cutoff_60:
        _recent_request_timestamps.popleft()
    while _recent_error_timestamps and _recent_error_timestamps[0] < cutoff_60:
        _recent_error_timestamps.popleft()

    stale_routes: list[str] = []
    for route_path, route_metric in _routes.items():
        route_recent = route_metric.get("recent_timestamps")
        if isinstance(route_recent, deque):
            while route_recent and route_recent[0] < cutoff_60:
                route_recent.popleft()

        # Drop long-idle routes to keep memory bounded.
        last_seen_at = float(route_metric.get("last_seen_at", 0.0) or 0.0)
        if not route_recent and now - last_seen_at > WINDOW_SECONDS * 3:
            stale_routes.append(route_path)

    for route_path in stale_routes:
        _routes.pop(route_path, None)

    for dep_metric in _dependencies.values():
        dep_recent = dep_metric.get("recent")
        if isinstance(dep_recent, deque):
            while dep_recent and dep_recent[0][0] < cutoff_60:
                dep_recent.popleft()


def request_started() -> float:
    global _inflight_requests, _peak_inflight_requests
    start = time.perf_counter()
    with _lock:
        _inflight_requests += 1
        if _inflight_requests > _peak_inflight_requests:
            _peak_inflight_requests = _inflight_requests
    return start


def request_finished(path: str, status_code: int, started_at: float) -> None:
    global _inflight_requests, _total_requests, _total_errors, _total_duration_ms

    now = time.monotonic()
    duration_ms = max(0.0, (time.perf_counter() - started_at) * 1000.0)
    normalized_path = str(path or "").strip() or "/"

    with _lock:
        _inflight_requests = max(0, _inflight_requests - 1)
        _total_requests += 1
        _total_duration_ms += duration_ms
        _recent_request_timestamps.append(now)
        _recent_latency_ms.append(duration_ms)

        is_error = int(status_code or 0) >= 500
        if is_error:
            _total_errors += 1
            _recent_error_timestamps.append(now)

        route_metric = _routes.setdefault(
            normalized_path,
            {
                "total_requests": 0,
                "total_errors": 0,
                "total_duration_ms": 0.0,
                "recent_timestamps": deque(maxlen=MAX_ROUTE_RECENT_SAMPLES),
                "recent_latency_ms": deque(maxlen=MAX_ROUTE_LATENCY_SAMPLES),
                "last_status_code": 0,
                "last_seen_at": 0.0,
            },
        )
        route_metric["total_requests"] = int(route_metric["total_requests"]) + 1
        route_metric["total_duration_ms"] = float(route_metric["total_duration_ms"]) + duration_ms
        route_metric["recent_timestamps"].append(now)
        route_metric["recent_latency_ms"].append(duration_ms)
        route_metric["last_status_code"] = int(status_code or 0)
        route_metric["last_seen_at"] = now
        if is_error:
            route_metric["total_errors"] = int(route_metric["total_errors"]) + 1

        _prune_locked(now)


def observe_dependency_latency(name: str, latency_ms: float | None, ok: bool) -> None:
    normalized_name = str(name or "").strip().lower()
    if not normalized_name:
        return

    now = time.monotonic()
    recorded_latency = None if latency_ms is None else max(0.0, float(latency_ms))

    with _lock:
        dep_metric = _dependencies.setdefault(
            normalized_name,
            {
                "last_ok": True,
                "last_latency_ms": None,
                "sample_count": 0,
                "failure_count": 0,
                "recent": deque(maxlen=MAX_LATENCY_SAMPLES),  # (monotonic_ts, latency_ms, ok)
            },
        )
        dep_metric["last_ok"] = bool(ok)
        dep_metric["last_latency_ms"] = recorded_latency
        dep_metric["sample_count"] = int(dep_metric["sample_count"]) + 1
        if not ok:
            dep_metric["failure_count"] = int(dep_metric["failure_count"]) + 1
        dep_metric["recent"].append((now, recorded_latency, bool(ok)))
        _prune_locked(now)


def _rate_per_second(samples: deque[float], now: float, window_seconds: float) -> float:
    if window_seconds <= 0:
        return 0.0
    cutoff = now - window_seconds
    count = 0
    for ts in reversed(samples):
        if ts < cutoff:
            break
        count += 1
    return round(count / window_seconds, 3)


def _percentile(values: list[float], q: float) -> float:
    if not values:
        return 0.0
    if q <= 0:
        return float(min(values))
    if q >= 1:
        return float(max(values))
    ordered = sorted(values)
    idx = int(round((len(ordered) - 1) * q))
    return float(ordered[max(0, min(idx, len(ordered) - 1))])


def _summarize_dependency(metric: dict[str, Any], now: float) -> dict[str, Any]:
    recent = metric.get("recent")
    if not isinstance(recent, deque):
        return {
            "available": bool(metric.get("last_ok", False)),
            "last_latency_ms": metric.get("last_latency_ms"),
            "avg_latency_ms_60s": 0.0,
            "p95_latency_ms_60s": 0.0,
            "failure_rate_60s": 0.0,
            "samples_60s": 0,
        }

    cutoff = now - WINDOW_SECONDS
    samples_60s = [entry for entry in recent if entry[0] >= cutoff]
    latency_samples = [float(entry[1]) for entry in samples_60s if entry[1] is not None]
    failures = sum(1 for entry in samples_60s if not bool(entry[2]))
    sample_count = len(samples_60s)

    return {
        "available": bool(metric.get("last_ok", False)),
        "last_latency_ms": metric.get("last_latency_ms"),
        "avg_latency_ms_60s": round(statistics.fmean(latency_samples), 3) if latency_samples else 0.0,
        "p95_latency_ms_60s": round(_percentile(latency_samples, 0.95), 3) if latency_samples else 0.0,
        "failure_rate_60s": round((failures / sample_count) * 100.0, 3) if sample_count else 0.0,
        "samples_60s": sample_count,
    }


def snapshot() -> dict[str, Any]:
    now = time.monotonic()
    with _lock:
        _prune_locked(now)
        uptime_seconds = max(0.0, now - _started_at)
        avg_response_ms = (_total_duration_ms / _total_requests) if _total_requests else 0.0
        recent_latency = list(_recent_latency_ms)
        rps_10 = _rate_per_second(_recent_request_timestamps, now, SHORT_WINDOW_SECONDS)
        rps_60 = _rate_per_second(_recent_request_timestamps, now, WINDOW_SECONDS)
        err_rps_60 = _rate_per_second(_recent_error_timestamps, now, WINDOW_SECONDS)

        route_rows: list[dict[str, Any]] = []
        for route_path, metric in _routes.items():
            route_count = int(metric.get("total_requests", 0) or 0)
            if route_count <= 0:
                continue
            route_errors = int(metric.get("total_errors", 0) or 0)
            route_total_ms = float(metric.get("total_duration_ms", 0.0) or 0.0)
            route_recent_timestamps = metric.get("recent_timestamps")
            route_recent_latency = metric.get("recent_latency_ms")

            route_recent_count_60 = 0
            if isinstance(route_recent_timestamps, deque):
                cutoff = now - WINDOW_SECONDS
                for ts in reversed(route_recent_timestamps):
                    if ts < cutoff:
                        break
                    route_recent_count_60 += 1

            latency_values = list(route_recent_latency) if isinstance(route_recent_latency, deque) else []
            route_rows.append(
                {
                    "path": route_path,
                    "total_requests": route_count,
                    "total_errors": route_errors,
                    "error_rate_percent": round((route_errors / route_count) * 100.0, 3) if route_count else 0.0,
                    "avg_response_ms": round(route_total_ms / route_count, 3) if route_count else 0.0,
                    "p95_response_ms": round(_percentile(latency_values, 0.95), 3) if latency_values else 0.0,
                    "rps_60s": round(route_recent_count_60 / WINDOW_SECONDS, 3),
                    "last_status_code": int(metric.get("last_status_code", 0) or 0),
                }
            )

        route_rows.sort(
            key=lambda item: (
                float(item.get("rps_60s", 0.0) or 0.0),
                int(item.get("total_requests", 0) or 0),
            ),
            reverse=True,
        )

        dependencies = {
            dependency_name: _summarize_dependency(dependency_metric, now)
            for dependency_name, dependency_metric in _dependencies.items()
        }

        return {
            "uptime_seconds": round(uptime_seconds, 3),
            "inflight_requests": _inflight_requests,
            "peak_inflight_requests": _peak_inflight_requests,
            "total_requests": _total_requests,
            "total_errors": _total_errors,
            "error_rate_percent": round((_total_errors / _total_requests) * 100.0, 3) if _total_requests else 0.0,
            "rps_10s": rps_10,
            "rps_60s": rps_60,
            "error_rps_60s": err_rps_60,
            "avg_response_ms": round(avg_response_ms, 3),
            "p95_response_ms": round(_percentile(recent_latency, 0.95), 3) if recent_latency else 0.0,
            "routes": route_rows[:MAX_TOP_ROUTES],
            "dependencies": dependencies,
        }
