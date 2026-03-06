from __future__ import annotations

import argparse
import asyncio
import random
import statistics
import time
from collections import deque
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any

import aiohttp


@dataclass(frozen=True)
class LoadProfile:
    path: str
    weight: float
    method: str = "GET"


LOAD_PROFILES: tuple[LoadProfile, ...] = (
    LoadProfile(path="/api/auth/session/me?lite=true", weight=0.60),
    LoadProfile(path="/api/auth/economy/overview", weight=0.20),
    LoadProfile(path="/api/auth/voting/session", weight=0.10),
    LoadProfile(path="/api/auth/marketplace", weight=0.10),
)


def percentile(values: list[float], quantile: float) -> float:
    if not values:
        return 0.0
    if quantile <= 0:
        return min(values)
    if quantile >= 1:
        return max(values)
    ordered = sorted(values)
    idx = int(round((len(ordered) - 1) * quantile))
    idx = max(0, min(idx, len(ordered) - 1))
    return ordered[idx]


class AggregateStats:
    def __init__(self) -> None:
        self._lock = asyncio.Lock()
        self.started_at = time.monotonic()
        self.completed_requests = 0
        self.success_requests = 0
        self.failed_requests = 0
        self.login_failures = 0
        self.active_workers = 0
        self.latency_ms: deque[float] = deque(maxlen=8000)
        self.status_counts: dict[int, int] = {}

    async def set_active_workers(self, active_workers: int) -> None:
        async with self._lock:
            self.active_workers = max(0, int(active_workers))

    async def worker_started(self) -> None:
        async with self._lock:
            self.active_workers += 1

    async def worker_finished(self) -> None:
        async with self._lock:
            self.active_workers = max(0, self.active_workers - 1)

    async def record_login_failure(self) -> None:
        async with self._lock:
            self.login_failures += 1

    async def record_request(self, status_code: int, elapsed_ms: float) -> None:
        async with self._lock:
            self.completed_requests += 1
            if 200 <= int(status_code) < 400:
                self.success_requests += 1
            else:
                self.failed_requests += 1
            self.status_counts[int(status_code)] = self.status_counts.get(int(status_code), 0) + 1
            self.latency_ms.append(max(0.0, float(elapsed_ms)))

    async def snapshot(self) -> dict[str, Any]:
        async with self._lock:
            elapsed = max(0.001, time.monotonic() - self.started_at)
            latencies = list(self.latency_ms)
            p95 = percentile(latencies, 0.95) if latencies else 0.0
            avg = statistics.fmean(latencies) if latencies else 0.0
            return {
                "elapsed_seconds": elapsed,
                "completed_requests": self.completed_requests,
                "success_requests": self.success_requests,
                "failed_requests": self.failed_requests,
                "login_failures": self.login_failures,
                "active_workers": self.active_workers,
                "rps": self.completed_requests / elapsed,
                "avg_latency_ms": avg,
                "p95_latency_ms": p95,
                "status_counts": dict(self.status_counts),
            }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run concurrent authenticated load against Enigma and print live runtime counters."
    )
    parser.add_argument("--base-url", default="http://localhost:5241", help="Base app URL.")
    parser.add_argument("--username", required=True, help="Existing login username.")
    parser.add_argument("--password", required=True, help="Existing login password.")
    parser.add_argument("--users", type=int, default=60, help="Concurrent virtual users.")
    parser.add_argument("--duration-seconds", type=int, default=180, help="Total test duration.")
    parser.add_argument("--think-ms", type=int, default=140, help="Delay between requests per worker.")
    parser.add_argument("--spawn-delay-ms", type=int, default=25, help="Delay while spawning users.")
    parser.add_argument("--dashboard-interval-seconds", type=float, default=2.0, help="Live print cadence.")
    parser.add_argument("--request-timeout-seconds", type=float, default=12.0, help="HTTP timeout.")
    return parser.parse_args()


def normalize_base_url(base_url: str) -> str:
    return str(base_url or "").strip().rstrip("/")


async def login(session: aiohttp.ClientSession, base_url: str, username: str, password: str) -> bool:
    payload = {"username": username, "password": password, "rememberMe": False}
    try:
        async with session.post(f"{base_url}/api/auth/session/login", json=payload) as response:
            await response.read()
            return 200 <= response.status < 300
    except Exception:
        return False


def choose_profile() -> LoadProfile:
    total_weight = sum(profile.weight for profile in LOAD_PROFILES)
    pick = random.random() * total_weight
    cumulative = 0.0
    for profile in LOAD_PROFILES:
        cumulative += profile.weight
        if pick <= cumulative:
            return profile
    return LOAD_PROFILES[-1]


async def worker(
    worker_id: int,
    base_url: str,
    username: str,
    password: str,
    stop_at: float,
    stats: AggregateStats,
    timeout_seconds: float,
    think_ms: int,
) -> None:
    timeout = aiohttp.ClientTimeout(total=timeout_seconds)
    connector = aiohttp.TCPConnector(limit=0, limit_per_host=0, enable_cleanup_closed=True)
    async with aiohttp.ClientSession(timeout=timeout, connector=connector) as session:
        if not await login(session, base_url, username, password):
            await stats.record_login_failure()
            return

        await stats.worker_started()
        try:
            while time.monotonic() < stop_at:
                profile = choose_profile()
                request_started = time.perf_counter()
                status_code = 0
                try:
                    if profile.method == "GET":
                        async with session.get(f"{base_url}{profile.path}") as response:
                            status_code = response.status
                            await response.read()
                    else:
                        async with session.request(profile.method, f"{base_url}{profile.path}") as response:
                            status_code = response.status
                            await response.read()
                except Exception:
                    status_code = 0

                elapsed_ms = (time.perf_counter() - request_started) * 1000.0
                await stats.record_request(status_code, elapsed_ms)

                jitter_factor = random.uniform(0.55, 1.45)
                await asyncio.sleep(max(0.0, (think_ms * jitter_factor) / 1000.0))
        finally:
            await stats.worker_finished()


async def dashboard_loop(
    base_url: str,
    username: str,
    password: str,
    stats: AggregateStats,
    stop_at: float,
    timeout_seconds: float,
    interval_seconds: float,
) -> None:
    timeout = aiohttp.ClientTimeout(total=max(3.0, timeout_seconds))
    async with aiohttp.ClientSession(timeout=timeout) as session:
        dashboard_auth_ok = await login(session, base_url, username, password)
        while time.monotonic() < stop_at:
            await asyncio.sleep(max(0.5, interval_seconds))
            client_snapshot = await stats.snapshot()

            backend_runtime = {}
            backend_dependencies = {}
            if dashboard_auth_ok:
                try:
                    async with session.get(f"{base_url}/api/auth/system/perf") as response:
                        if 200 <= response.status < 300:
                            payload = await response.json(content_type=None)
                            backend_runtime = dict(payload.get("runtime", {}) or {})
                            backend_dependencies = dict(payload.get("dependencies", {}) or {})
                except Exception:
                    pass

            redis = dict(backend_dependencies.get("redis", {}) or {})
            mongo = dict(backend_dependencies.get("mongo", {}) or {})
            now_label = datetime.now(timezone.utc).strftime("%H:%M:%S")

            print(
                f"[{now_label} UTC] "
                f"client_rps={client_snapshot['rps']:.2f} "
                f"p95={client_snapshot['p95_latency_ms']:.1f}ms "
                f"ok={client_snapshot['success_requests']} "
                f"fail={client_snapshot['failed_requests']} "
                f"workers={client_snapshot['active_workers']} | "
                f"backend_rps10={float(backend_runtime.get('rps_10s', 0.0)):.2f} "
                f"inflight={int(backend_runtime.get('inflight_requests', 0))} "
                f"redis={float(redis.get('latency_ms', 0.0)):.1f}ms "
                f"mongo_wait={float(mongo.get('wait_estimate_ms', 0.0)):.1f}ms"
            )


async def run() -> int:
    args = parse_args()
    base_url = normalize_base_url(args.base_url)
    duration_seconds = max(10, int(args.duration_seconds))
    users = max(1, int(args.users))
    think_ms = max(0, int(args.think_ms))
    spawn_delay = max(0, int(args.spawn_delay_ms))
    timeout_seconds = max(3.0, float(args.request_timeout_seconds))
    interval_seconds = max(0.5, float(args.dashboard_interval_seconds))

    print(f"Starting load test against {base_url}")
    print(f"Users={users}, duration={duration_seconds}s, think={think_ms}ms")

    stats = AggregateStats()
    stop_at = time.monotonic() + duration_seconds
    worker_tasks: list[asyncio.Task[None]] = []

    dashboard_task = asyncio.create_task(
        dashboard_loop(
            base_url=base_url,
            username=args.username,
            password=args.password,
            stats=stats,
            stop_at=stop_at,
            timeout_seconds=timeout_seconds,
            interval_seconds=interval_seconds,
        )
    )

    for worker_id in range(users):
        worker_tasks.append(
            asyncio.create_task(
                worker(
                    worker_id=worker_id,
                    base_url=base_url,
                    username=args.username,
                    password=args.password,
                    stop_at=stop_at,
                    stats=stats,
                    timeout_seconds=timeout_seconds,
                    think_ms=think_ms,
                )
            )
        )
        if spawn_delay > 0:
            await asyncio.sleep(spawn_delay / 1000.0)

    await asyncio.gather(*worker_tasks, return_exceptions=True)
    await dashboard_task

    final_snapshot = await stats.snapshot()
    print("\nFinal summary")
    print(f"Elapsed: {final_snapshot['elapsed_seconds']:.1f}s")
    print(f"Requests: {final_snapshot['completed_requests']}")
    print(f"Success: {final_snapshot['success_requests']}")
    print(f"Failed: {final_snapshot['failed_requests']}")
    print(f"Login failures: {final_snapshot['login_failures']}")
    print(f"Average RPS: {final_snapshot['rps']:.2f}")
    print(f"Avg latency: {final_snapshot['avg_latency_ms']:.1f}ms")
    print(f"P95 latency: {final_snapshot['p95_latency_ms']:.1f}ms")
    print(f"Status counts: {final_snapshot['status_counts']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(run()))
