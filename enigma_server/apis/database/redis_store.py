from __future__ import annotations

import json
import os
from contextlib import contextmanager
from functools import lru_cache
from typing import Any

from fastapi import HTTPException
from redis import Redis
from redis.exceptions import RedisError


DEFAULT_REDIS_URL = "redis://localhost:6379/0"
SESSION_TTL_SECONDS = 60 * 60 * 6
COMPLETED_SESSION_TTL_SECONDS = 60 * 60


def get_redis_url() -> str:
    return os.getenv("REDIS_URL", DEFAULT_REDIS_URL).strip() or DEFAULT_REDIS_URL


@lru_cache(maxsize=1)
def get_redis_client() -> Redis:
    return Redis.from_url(get_redis_url(), decode_responses=True)


def ensure_redis_available() -> Redis:
    client = get_redis_client()
    try:
        client.ping()
    except RedisError as exc:
        raise HTTPException(status_code=503, detail=f"Redis unavailable: {exc}") from exc
    return client


def session_key(session_id: str) -> str:
    return f"enigma:multiplayer:session:{session_id}"


def user_session_key(username: str) -> str:
    return f"enigma:multiplayer:user-session:{username.strip().lower()}"


def load_json(key: str) -> dict[str, Any] | None:
    client = ensure_redis_available()
    raw = client.get(key)
    if not raw:
        return None

    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        client.delete(key)
        return None


def save_json(key: str, payload: dict[str, Any], ttl_seconds: int = SESSION_TTL_SECONDS) -> None:
    client = ensure_redis_available()
    client.set(key, json.dumps(payload), ex=ttl_seconds)


def delete_keys(*keys: str) -> None:
    existing = [key for key in keys if key]
    if not existing:
        return

    client = ensure_redis_available()
    client.delete(*existing)


@contextmanager
def session_lock(session_id: str):
    client = ensure_redis_available()
    lock = client.lock(f"{session_key(session_id)}:lock", timeout=5, blocking_timeout=2)
    acquired = False
    try:
        acquired = lock.acquire(blocking=True)
        if not acquired:
            raise HTTPException(status_code=409, detail="Multiplayer session is busy, try again.")
        yield
    finally:
        if acquired:
            try:
                lock.release()
            except RedisError:
                pass
