from __future__ import annotations

import asyncio
import json
from datetime import datetime, timedelta, timezone
from typing import Any
from uuid import uuid4

from anyio import from_thread
from fastapi import APIRouter, HTTPException, Request, WebSocket, WebSocketDisconnect
from pydantic import BaseModel, Field, ValidationError

from main import limiter

from apis.maze.maze import parse_seed, room_types

from .db import maps_collection, users_collection
from .economy_rules import compute_loss_fee, compute_multiplayer_rewards, credit_bank_dividend
from .multiplayer_puzzles import CO_OP_PUZZLE_CATALOG
from .multiplayer_runtime import (
    apply_puzzle_action,
    ensure_current_room_puzzle_state,
    serialize_current_room_puzzle,
    update_position_puzzle_state,
)
from .redis_store import (
    COMPLETED_SESSION_TTL_SECONDS,
    SESSION_TTL_SECONDS,
    delete_keys,
    load_json,
    remove_user_invite,
    save_json,
    upsert_user_invite,
    session_key,
    session_lock,
    user_invites_key,
    user_session_key,
)


router = APIRouter(prefix="/database/multiplayer")
_SESSION_SOCKETS: dict[str, list[tuple[str, WebSocket]]] = {}
_PENDING_SOCKET_ABANDON_TASKS: dict[tuple[str, str], asyncio.Task[None]] = {}
SOCKET_ABANDON_GRACE_SECONDS = 4.0


class MultiplayerCreatePayload(BaseModel):
    username: str
    seed: str
    map_name: str | None = None
    source: str = "new"
    invited_friends: list[str] = []


class MultiplayerInvitePayload(BaseModel):
    username: str
    session_id: str
    friend_username: str


class MultiplayerJoinPayload(BaseModel):
    username: str
    session_id: str


class MultiplayerReadyPayload(BaseModel):
    username: str
    session_id: str
    ready: bool = True


class MultiplayerPositionPayload(BaseModel):
    x: float = Field(ge=0)
    y: float = Field(ge=0)
    width: float = Field(default=8, ge=0)
    height: float = Field(default=8, ge=0)
    x_percent: float = Field(default=50, ge=0, le=100)
    y_percent: float = Field(default=50, ge=0, le=100)


class MultiplayerStatePayload(BaseModel):
    username: str
    session_id: str
    room_x: int
    room_y: int
    position: MultiplayerPositionPayload
    facing: str = "Down"
    is_on_black_hole: bool = False
    gold_collected: int = Field(default=0, ge=0)
    puzzle_solved: bool = False
    reward_pickup_collected: bool = False


class MultiplayerMovePayload(BaseModel):
    username: str
    session_id: str
    target_room_x: int
    target_room_y: int


class MultiplayerFinishPayload(BaseModel):
    username: str
    session_id: str


class MultiplayerLeavePayload(BaseModel):
    username: str
    session_id: str
    reason: str = "left_session"


class MultiplayerPuzzleActionPayload(BaseModel):
    username: str
    session_id: str
    action: str
    args: dict[str, Any] = {}


class MultiplayerSyncSavedMapPayload(BaseModel):
    username: str
    session_id: str
    map_name: str | None = None


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _utc_now_iso() -> str:
    return _utc_now().isoformat()


def _default_position() -> dict[str, Any]:
    return {
        "x": 46.0,
        "y": 46.0,
        "width": 8.0,
        "height": 8.0,
        "x_percent": 50.0,
        "y_percent": 50.0,
    }


def _stable_hash(value: str) -> int:
    hash_value = 2166136261
    for character in value:
        hash_value ^= ord(character)
        hash_value = (hash_value * 16777619) & 0xFFFFFFFF
    return hash_value & 0x7FFFFFFF


def _apply_reward_difficulty(value: int, difficulty: str) -> int:
    normalized = (difficulty or "").strip().lower()
    if normalized == "medium":
        return int(round(value * 1.25))
    if normalized == "hard":
        return value * 2
    return value


def _get_room_progress(session: dict[str, Any], room_key: str) -> dict[str, Any]:
    room_progress = session.setdefault("room_progress", {})
    progress = room_progress.get(room_key)
    if isinstance(progress, dict):
        return progress

    progress = {
        "puzzle_solved": False,
        "reward_pickup_collected": False,
    }
    room_progress[room_key] = progress
    return progress


def _get_puzzle_reward(seed: str, difficulty: str, room_x: int, room_y: int) -> int:
    base_reward = 18 + (_stable_hash(f"reward|{seed}|{room_x}|{room_y}") % 11)
    return _apply_reward_difficulty(base_reward, difficulty)


def _get_reward_pickup_bonus(seed: str, difficulty: str, room_x: int, room_y: int) -> int:
    base_reward = 24 + (_stable_hash(f"bonus|{seed}|{room_x}|{room_y}") % 19)
    return _apply_reward_difficulty(base_reward, difficulty)


def _finalize_room_puzzle_progress(session: dict[str, Any], room_key: str) -> None:
    room_progress = _get_room_progress(session, room_key)
    if room_progress.get("puzzle_solved"):
        return

    current_puzzle = ensure_current_room_puzzle_state(session)
    if not current_puzzle.get("completed"):
        return

    room = session.get("room_lookup", {}).get(room_key) or {}
    room_progress["puzzle_solved"] = True
    session["team_gold"] = int(session.get("team_gold", 0) or 0) + _get_puzzle_reward(
        session.get("seed", ""),
        session.get("difficulty", ""),
        int(room.get("x", 0) or 0),
        int(room.get("y", 0) or 0),
    )


def _parse_seed_layout(seed: str) -> dict[str, Any]:
    trimmed = (seed or "").strip()
    if not trimmed or "-" not in trimmed:
        raise HTTPException(status_code=400, detail="A valid difficulty-prefixed seed is required")

    difficulty, raw_seed = trimmed.split("-", 1)
    parsed, errors = parse_seed(raw_seed)
    if errors or not parsed:
        raise HTTPException(status_code=400, detail="Invalid maze seed")

    room_lookup: dict[str, dict[str, Any]] = {}
    start_room: dict[str, int] | None = None
    finish_room: dict[str, int] | None = None

    for room in parsed["rooms"]:
        x, y = room["cord"]
        connections = room_types[room["room_type"]]
        room_lookup[f"{x},{y}"] = {
            "x": x,
            "y": y,
            "room_type": room["room_type"],
            "puzzle_key": room["puzzle_type"],
            "kind": room["type"],
            "doors": {
                "north": bool(connections[0]),
                "east": bool(connections[1]),
                "south": bool(connections[2]),
                "west": bool(connections[3]),
            },
        }

        if room["type"] == "S":
            start_room = {"x": x, "y": y}
        elif room["type"] == "F":
            finish_room = {"x": x, "y": y}

    if start_room is None or finish_room is None:
        raise HTTPException(status_code=400, detail="Seed must contain a start room and finish room")

    return {
        "difficulty": difficulty.strip().lower(),
        "size": int(parsed["size"] or 0),
        "start_room": start_room,
        "finish_room": finish_room,
        "rooms": room_lookup,
    }


def _normalize_username(username: str) -> str:
    return (username or "").strip()


def _load_user(username: str) -> dict[str, Any]:
    normalized = _normalize_username(username)
    user = users_collection.find_one({"username": normalized})
    if not user:
        raise HTTPException(status_code=404, detail="User not found")

    if bool(user.get("is_system_account")):
        raise HTTPException(status_code=403, detail="System accounts cannot use multiplayer")

    return user


def _get_active_session_id_for_user(username: str) -> str | None:
    session_id = load_json(user_session_key(username))
    if isinstance(session_id, dict):
        return str(session_id.get("session_id") or "").strip() or None

    if isinstance(session_id, str):
        return session_id.strip() or None

    return None


def _store_user_session(username: str, session_id: str) -> None:
    save_json(user_session_key(username), {"session_id": session_id}, ttl_seconds=SESSION_TTL_SECONDS)


def _clear_user_session(username: str) -> None:
    delete_keys(user_session_key(username))


def _build_invite_payload(session: dict[str, Any]) -> dict[str, Any]:
    return {
        "session_id": session.get("session_id"),
        "owner_username": session.get("owner_username"),
        "map_name": session.get("map_name"),
        "difficulty": session.get("difficulty"),
        "size": int(session.get("size", 0) or 0),
        "status": session.get("status"),
        "created_at": session.get("created_at"),
        "source": session.get("source"),
    }


def _store_pending_invite(friend_username: str, session: dict[str, Any]) -> None:
    upsert_user_invite(friend_username, str(session.get("session_id") or ""), _build_invite_payload(session))


def _clear_pending_invites(session: dict[str, Any], *usernames: str) -> None:
    session_id = str(session.get("session_id") or "").strip()
    if not session_id:
        return

    invitees = set(session.get("invited_friends", []))
    invitees.update(username for username in usernames if username)
    for username in invitees:
        remove_user_invite(username, session_id)


def _register_session_socket(session_id: str, username: str, websocket: WebSocket) -> None:
    _cancel_pending_socket_abandon(session_id, username)
    sockets = _SESSION_SOCKETS.setdefault(session_id, [])
    sockets.append((username, websocket))


def _unregister_session_socket(session_id: str, websocket: WebSocket) -> list[str]:
    sockets = _SESSION_SOCKETS.get(session_id)
    if not sockets:
        return []

    disconnected_usernames = [
        username
        for username, entry in sockets
        if entry is websocket
    ]
    remaining = [(username, entry) for username, entry in sockets if entry is not websocket]
    if remaining:
        _SESSION_SOCKETS[session_id] = remaining
    else:
        _SESSION_SOCKETS.pop(session_id, None)

    return disconnected_usernames


def _cancel_pending_socket_abandon(session_id: str, username: str) -> None:
    task_key = (session_id, _normalize_username(username))
    task = _PENDING_SOCKET_ABANDON_TASKS.pop(task_key, None)
    if task is not None and not task.done():
        task.cancel()


async def _abandon_session_after_socket_disconnect(session_id: str, username: str) -> None:
    task_key = (session_id, _normalize_username(username))

    try:
        await asyncio.sleep(SOCKET_ABANDON_GRACE_SECONDS)

        with session_lock(session_id):
            session = load_json(session_key(session_id))
            if not session or str(session.get("status") or "").strip().lower() != "active":
                return

            if not _session_has_member(session, username):
                _clear_user_session(username)
                return

            if _session_has_live_socket(session_id, username):
                return

            _leave_multiplayer_session_locked(session_id, session, _normalize_username(username), "socket_disconnect")

        _broadcast_ws_session_from_thread(session)
    except asyncio.CancelledError:
        pass
    finally:
        _PENDING_SOCKET_ABANDON_TASKS.pop(task_key, None)


def _schedule_socket_abandon(session_id: str, username: str) -> None:
    normalized_username = _normalize_username(username)
    if not session_id or not normalized_username:
        return

    _cancel_pending_socket_abandon(session_id, normalized_username)
    _PENDING_SOCKET_ABANDON_TASKS[(session_id, normalized_username)] = asyncio.create_task(
        _abandon_session_after_socket_disconnect(session_id, normalized_username)
    )


async def _send_ws_payload(websocket: WebSocket, payload: dict[str, Any]) -> bool:
    try:
        await websocket.send_json(payload)
        return True
    except Exception:
        return False


async def _send_ws_error(websocket: WebSocket, detail: str, status_code: int = 400) -> None:
    await _send_ws_payload(
        websocket,
        {
            "type": "error",
            "status": "error",
            "detail": detail,
            "status_code": status_code,
        },
    )


async def _broadcast_ws_session(
    session: dict[str, Any],
    room_moved: bool = False,
    completion: dict[str, Any] | None = None,
) -> None:
    session_id = str(session.get("session_id") or "").strip()
    if not session_id:
        return

    sockets = list(_SESSION_SOCKETS.get(session_id, []))
    if not sockets:
        return

    dead_sockets: list[WebSocket] = []
    for username, websocket in sockets:
        payload = {
            "type": "session",
            "status": "success",
            "room_moved": room_moved,
            "session": _serialize_session_for_user(session, username),
            "completion": completion or session.get("completion"),
        }
        if not await _send_ws_payload(websocket, payload):
            dead_sockets.append(websocket)

    for websocket in dead_sockets:
        for username in _unregister_session_socket(session_id, websocket):
            _schedule_socket_abandon(session_id, username)


def _broadcast_ws_session_from_thread(
    session: dict[str, Any],
    room_moved: bool = False,
    completion: dict[str, Any] | None = None,
) -> None:
    session_id = str(session.get("session_id") or "").strip()
    if not session_id or session_id not in _SESSION_SOCKETS:
        return

    try:
        from_thread.run(_broadcast_ws_session, session, room_moved, completion)
    except Exception:
        pass


def _session_has_member(session: dict[str, Any], username: str) -> bool:
    players = session.get("players", {})
    if not isinstance(players, dict):
        return False

    normalized_username = _normalize_username(username)
    return normalized_username in {_normalize_username(player_username) for player_username in players.keys()}


def _session_has_live_socket(session_id: str, username: str | None = None) -> bool:
    sockets = _SESSION_SOCKETS.get(session_id, [])
    if not sockets:
        return False

    if username is None:
        return bool(sockets)

    normalized_username = _normalize_username(username)
    return any(_normalize_username(socket_username) == normalized_username for socket_username, _ in sockets)


def _parse_iso_datetime(value: Any) -> datetime | None:
    text = str(value or "").strip()
    if not text:
        return None

    try:
        return datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError:
        return None


def _session_is_structurally_invalid(session: dict[str, Any]) -> bool:
    players = session.get("players", {})
    if not isinstance(players, dict) or not players:
        return True

    owner_username = _normalize_username(str(session.get("owner_username") or ""))
    guest_username = _normalize_username(str(session.get("guest_username") or ""))
    normalized_players = {_normalize_username(player_username) for player_username in players.keys()}

    if not owner_username or owner_username not in normalized_players:
        return True

    if guest_username and guest_username not in normalized_players:
        return True

    status = str(session.get("status") or "").strip().lower()
    if status in {"ready_check", "active"} and len(normalized_players) < 2:
        return True

    return False


def _all_players_stale(session: dict[str, Any], stale_after: timedelta) -> bool:
    players = session.get("players", {})
    if not isinstance(players, dict) or not players:
        return True

    now = _utc_now()
    for player in players.values():
        seen_at = _parse_iso_datetime((player or {}).get("last_seen_at"))
        if seen_at is None:
            return False
        if now - seen_at <= stale_after:
            return False

    return True


def _player_is_stale(session: dict[str, Any], username: str, stale_after: timedelta) -> bool:
    players = session.get("players", {})
    if not isinstance(players, dict):
        return True

    player = players.get(_normalize_username(username))
    if not isinstance(player, dict):
        return True

    seen_at = _parse_iso_datetime(player.get("last_seen_at"))
    if seen_at is None:
        return False

    return _utc_now() - seen_at > stale_after


def _soft_close_stale_session(session_id: str, session: dict[str, Any], reason: str) -> None:
    session["status"] = "abandoned"
    session["completed_at"] = _utc_now_iso()
    session["abandon_reason"] = reason
    session["abandoned_by"] = None
    save_json(session_key(session_id), session, ttl_seconds=COMPLETED_SESSION_TTL_SECONDS)
    _clear_pending_invites(session)

    for player_username in session.get("players", {}).keys():
        _clear_user_session(player_username)


def _session_should_soft_close(session_id: str, session: dict[str, Any], username: str) -> bool:
    status = str(session.get("status") or "").strip().lower()
    players = session.get("players", {})
    has_guest = bool(_normalize_username(str(session.get("guest_username") or "")))

    if _session_is_structurally_invalid(session):
        return True

    if status == "waiting_for_guest":
        return (not has_guest) or len(players) <= 1

    if status == "ready_check":
        return (not has_guest) or len(players) < 2

    if status == "active":
        if _all_players_stale(session, stale_after=timedelta(minutes=3)):
            return True

        if not _session_has_live_socket(session_id) and _all_players_stale(session, stale_after=timedelta(seconds=20)):
            return True

        if not _session_has_live_socket(session_id, username) and _player_is_stale(
            session,
            username,
            stale_after=timedelta(seconds=20),
        ):
            return True

    return False


def _ensure_user_available(username: str) -> None:
    active_session_id = _get_active_session_id_for_user(username)
    if not active_session_id:
        return

    existing_session = load_json(session_key(active_session_id))
    if (
        not existing_session
        or existing_session.get("status") in {"completed", "abandoned"}
        or not _session_has_member(existing_session, username)
    ):
        _clear_user_session(username)
        return

    if _session_should_soft_close(active_session_id, existing_session, username):
        with session_lock(active_session_id):
            existing_session = load_json(session_key(active_session_id))
            if not existing_session:
                _clear_user_session(username)
                return

            if (
                existing_session.get("status") in {"completed", "abandoned"}
                or not _session_has_member(existing_session, username)
                or _session_should_soft_close(active_session_id, existing_session, username)
            ):
                _soft_close_stale_session(active_session_id, existing_session, "stale_multiplayer_session")
                _clear_user_session(username)
                return

    raise HTTPException(status_code=409, detail="User is already in a multiplayer session")


def _create_player_state(username: str, role: str, room: dict[str, int]) -> dict[str, Any]:
    return {
        "username": username,
        "role": role,
        "joined_at": _utc_now_iso(),
        "ready": False,
        "last_seen_at": _utc_now_iso(),
        "room": {"x": room["x"], "y": room["y"]},
        "position": _default_position(),
        "facing": "Down",
        "is_on_black_hole": False,
        "gold_collected": 0,
    }


def _load_session_or_404(session_id: str) -> dict[str, Any]:
    payload = load_json(session_key(session_id))
    if not payload:
        raise HTTPException(status_code=404, detail="Multiplayer session not found")
    return payload


def _get_other_player(session: dict[str, Any], username: str) -> dict[str, Any] | None:
    for player_username, player in session.get("players", {}).items():
        if player_username != username:
            return player
    return None


def _serialize_session_for_user(session: dict[str, Any], username: str) -> dict[str, Any]:
    players = session.get("players", {})
    you = players.get(username)
    other = _get_other_player(session, username)
    current_room = session.get("current_room", {})
    other_visible = bool(other) and other.get("room") == current_room
    room_key = f"{current_room.get('x')},{current_room.get('y')}"
    current_room_progress = _get_room_progress(session, room_key)
    current_room_puzzle = (
        serialize_current_room_puzzle(session, username)
        if len(players) == 2 and session.get("guest_username")
        else None
    )
    solved_room_count = sum(
        1 for progress in session.get("room_progress", {}).values() if isinstance(progress, dict) and progress.get("puzzle_solved")
    )

    return {
        "session_id": session.get("session_id"),
        "status": session.get("status"),
        "owner_username": session.get("owner_username"),
        "guest_username": session.get("guest_username"),
        "seed": session.get("seed"),
        "map_name": session.get("map_name"),
        "source": session.get("source"),
        "difficulty": session.get("difficulty"),
        "size": session.get("size"),
        "team_gold": int(session.get("team_gold", 0) or 0),
        "solved_room_count": solved_room_count,
        "current_room": current_room,
        "current_room_progress": current_room_progress,
        "current_room_puzzle": current_room_puzzle,
        "start_room": session.get("start_room"),
        "finish_room": session.get("finish_room"),
        "created_at": session.get("created_at"),
        "started_at": session.get("started_at"),
        "completed_at": session.get("completed_at"),
        "invited_friends": session.get("invited_friends", []),
        "all_ready": all(player.get("ready") for player in players.values()) and len(players) == 2,
        "required_players": 2,
        "move_vote": session.get("move_vote"),
        "you": you,
        "other_player_visible": other_visible,
        "other_player": other if other_visible else None,
        "completion": session.get("completion"),
    }


def _assert_session_member(session: dict[str, Any], username: str) -> dict[str, Any]:
    players = session.get("players", {})
    player = players.get(username)
    if not player:
        raise HTTPException(status_code=403, detail="User is not part of this session")
    return player


def _assert_owner(session: dict[str, Any], username: str) -> None:
    if session.get("owner_username") != username:
        raise HTTPException(status_code=403, detail="Only the session owner can perform this action")


def _assert_joinable_friend(owner_user: dict[str, Any], friend_username: str) -> None:
    friends = {str(friend).strip() for friend in owner_user.get("friends", []) if str(friend).strip()}
    if friend_username not in friends:
        raise HTTPException(status_code=403, detail="You can only invite players who are already your friends")


def _can_move_to_target(session: dict[str, Any], target_x: int, target_y: int) -> bool:
    current_room = session.get("current_room", {})
    current_room_key = f"{current_room.get('x')},{current_room.get('y')}"
    target_key = f"{target_x},{target_y}"
    room_lookup = session.get("room_lookup", {})
    current = room_lookup.get(current_room_key)
    target = room_lookup.get(target_key)
    if not current or not target:
        return False

    dx = target_x - int(current_room.get("x", 0))
    dy = target_y - int(current_room.get("y", 0))
    if abs(dx) + abs(dy) != 1:
        return False

    if dx == 1:
        return bool(current["doors"].get("east")) and bool(target["doors"].get("west"))
    if dx == -1:
        return bool(current["doors"].get("west")) and bool(target["doors"].get("east"))
    if dy == 1:
        return bool(current["doors"].get("south")) and bool(target["doors"].get("north"))
    if dy == -1:
        return bool(current["doors"].get("north")) and bool(target["doors"].get("south"))

    return False


def _reset_players_to_room(session: dict[str, Any], room: dict[str, int]) -> None:
    for player in session.get("players", {}).values():
        player["room"] = {"x": room["x"], "y": room["y"]}
        player["position"] = _default_position()
        player["is_on_black_hole"] = False
        player["last_seen_at"] = _utc_now_iso()


def _update_multiplayer_state_locked(session: dict[str, Any], payload: MultiplayerStatePayload) -> None:
    username = _normalize_username(payload.username)
    player = _assert_session_member(session, username)

    current_room = session.get("current_room", {})
    if payload.room_x != current_room.get("x") or payload.room_y != current_room.get("y"):
        raise HTTPException(status_code=409, detail="Players must remain in the same room")

    player["room"] = {"x": payload.room_x, "y": payload.room_y}
    player["position"] = payload.position.model_dump(mode="json")
    player["facing"] = payload.facing
    player["is_on_black_hole"] = bool(payload.is_on_black_hole)
    player["last_seen_at"] = _utc_now_iso()

    room_key = f"{payload.room_x},{payload.room_y}"
    room_state = session.get("room_lookup", {}).get(room_key) or {}
    room_progress = _get_room_progress(session, room_key)
    team_gold = int(session.get("team_gold", 0) or 0)
    update_position_puzzle_state(session)
    _finalize_room_puzzle_progress(session, room_key)

    if payload.puzzle_solved and not room_progress.get("puzzle_solved"):
        room_progress["puzzle_solved"] = True
        team_gold += _get_puzzle_reward(session.get("seed", ""), session.get("difficulty", ""), payload.room_x, payload.room_y)

    if (
        payload.reward_pickup_collected
        and str(room_state.get("kind") or "").upper() == "R"
        and not room_progress.get("reward_pickup_collected")
    ):
        room_progress["reward_pickup_collected"] = True
        team_gold += _get_reward_pickup_bonus(session.get("seed", ""), session.get("difficulty", ""), payload.room_x, payload.room_y)

    session["team_gold"] = max(int(session.get("team_gold", 0) or 0), team_gold)
    for state in session.get("players", {}).values():
        state["gold_collected"] = int(session["team_gold"])


def _apply_multiplayer_puzzle_action_locked(session: dict[str, Any], payload: MultiplayerPuzzleActionPayload) -> None:
    username = _normalize_username(payload.username)
    _assert_session_member(session, username)

    if session.get("status") != "active":
        raise HTTPException(status_code=409, detail="Puzzle actions are only allowed in active sessions.")

    apply_puzzle_action(session, username, payload.action, payload.args)
    room_key = _room_key(session)
    _finalize_room_puzzle_progress(session, room_key)

    session["team_gold"] = int(session.get("team_gold", 0) or 0)
    for state in session.get("players", {}).values():
        state["gold_collected"] = int(session["team_gold"])


def _request_room_move_locked(session: dict[str, Any], payload: MultiplayerMovePayload) -> bool:
    username = _normalize_username(payload.username)
    target_room = {"x": payload.target_room_x, "y": payload.target_room_y}
    _assert_session_member(session, username)
    if session.get("status") != "active":
        raise HTTPException(status_code=409, detail="Room moves are only allowed after both players are ready")

    if not _can_move_to_target(session, target_room["x"], target_room["y"]):
        raise HTTPException(status_code=400, detail="Target room is not connected to the current room")

    players = session.get("players", {})
    if len(players) != 2:
        raise HTTPException(status_code=409, detail="Both players must be present before moving rooms")

    current_room = session.get("current_room", {})
    current_room_key = f"{current_room.get('x')},{current_room.get('y')}"
    current_room_progress = _get_room_progress(session, current_room_key)
    if not current_room_progress.get("puzzle_solved"):
        raise HTTPException(status_code=409, detail="The current room is still locked")

    move_vote = session.get("move_vote")
    target_key = f"{target_room['x']},{target_room['y']}"
    if move_vote and move_vote.get("target_key") != target_key and move_vote.get("votes"):
        other_votes = [vote for vote in move_vote.get("votes", []) if vote != username]
        if other_votes:
            raise HTTPException(status_code=409, detail="The other player requested a different room")

    if not move_vote or move_vote.get("target_key") != target_key:
        move_vote = {"target_key": target_key, "target": target_room, "votes": []}

    votes = set(move_vote.get("votes", []))
    votes.add(username)
    move_vote["votes"] = sorted(votes, key=str.casefold)

    room_moved = False
    if len(move_vote["votes"]) == 2:
        session["current_room"] = target_room
        _reset_players_to_room(session, target_room)
        move_vote = None
        room_moved = True

    session["move_vote"] = move_vote
    return room_moved


def _finish_multiplayer_session_locked(session_id: str, session: dict[str, Any], username: str) -> dict[str, Any]:
    _assert_session_member(session, username)

    if session.get("status") != "active":
        raise HTTPException(status_code=409, detail="Session is not active")

    players = session.get("players", {})
    if len(players) != 2 or not session.get("guest_username"):
        raise HTTPException(status_code=409, detail="Both players must be present to finish the run")

    current_room = session.get("current_room", {})
    finish_room = session.get("finish_room", {})
    if current_room != finish_room:
        raise HTTPException(status_code=409, detail="Both players must be in the finish room")

    if not all(bool(player.get("is_on_black_hole")) for player in players.values()):
        raise HTTPException(status_code=409, detail="Both players must stand on the black hole to finish")

    owner_username = session["owner_username"]
    guest_username = session["guest_username"]
    owner_user = _load_user(owner_username)
    guest_user = _load_user(guest_username)

    total_rewards = int(session.get("team_gold", 0) or 0)
    payout = compute_multiplayer_rewards(total_rewards, owner_user, guest_user)
    bank_dividend = int(payout["bank_dividend"] or 0)

    map_doc = maps_collection.find_one({"seed": session["seed"]})
    owner_update: dict[str, Any] = {
        "$inc": {
            "number_of_maps_played": 1,
            "maps_completed": 1,
            "maze_nuggets": int(payout["owner"]["rewarded_mn"]),
        }
    }
    guest_update: dict[str, Any] = {
        "$inc": {
            "number_of_maps_played": 1,
            "maps_completed": 1,
            "maze_nuggets": int(payout["guest"]["rewarded_mn"]),
        }
    }

    if map_doc:
        owner_update["$addToSet"] = {"maps_discovered": map_doc["_id"]}
        guest_update["$addToSet"] = {"maps_discovered": map_doc["_id"]}

    users_collection.update_one({"username": owner_username}, owner_update)
    users_collection.update_one({"username": guest_username}, guest_update)
    credit_bank_dividend(users_collection, bank_dividend)

    session["status"] = "completed"
    session["completed_at"] = _utc_now_iso()
    session["completion"] = {
        "total_rewards": total_rewards,
        "bank_dividend": bank_dividend,
        "owner_reward": int(payout["owner"]["rewarded_mn"]),
        "guest_reward": int(payout["guest"]["rewarded_mn"]),
        "discoverers": [owner_username, guest_username],
        "owner_username": owner_username,
        "seed_existed": bool(session.get("seed_existed")),
        "requires_owner_save": map_doc is None and session.get("source") == "new",
    }

    save_json(session_key(session_id), session, ttl_seconds=COMPLETED_SESSION_TTL_SECONDS)
    _clear_pending_invites(session)
    delete_keys(user_session_key(owner_username), user_session_key(guest_username))
    return session["completion"]


def _leave_multiplayer_session_locked(session_id: str, session: dict[str, Any], username: str, reason: str) -> None:
    _assert_session_member(session, username)

    fee_total = 0
    penalties: dict[str, Any] = {}
    for player_username in session.get("players", {}).keys():
        try:
            user = _load_user(player_username)
        except HTTPException:
            continue

        loss_fee = compute_loss_fee(user)
        fee_applied = int(loss_fee["applied_fee"] or 0)
        update_query: dict[str, Any] = {
            "$inc": {
                "number_of_maps_played": 1,
                "maps_lost": 1,
                "maze_nuggets": -fee_applied,
            }
        }
        users_collection.update_one({"username": player_username}, update_query)
        penalties[player_username] = {"loss_fee_applied": fee_applied}
        fee_total += fee_applied

    session["status"] = "abandoned"
    session["completed_at"] = _utc_now_iso()
    session["abandoned_by"] = username
    session["abandon_reason"] = (reason or "left_session").strip() or "left_session"
    session["abandon_penalties"] = penalties
    save_json(session_key(session_id), session, ttl_seconds=COMPLETED_SESSION_TTL_SECONDS)
    _clear_pending_invites(session)

    for player_username in session.get("players", {}).keys():
        _clear_user_session(player_username)

    credit_bank_dividend(users_collection, fee_total)


@router.get("/puzzle_catalog")
@limiter.limit("30/minute")
def get_multiplayer_puzzle_catalog(request: Request):
    return {"status": "success", "catalog": CO_OP_PUZZLE_CATALOG}


@router.post("/session/create")
@limiter.limit("20/minute")
def create_multiplayer_session(request: Request, payload: MultiplayerCreatePayload):
    owner_username = _normalize_username(payload.username)
    owner_user = _load_user(owner_username)
    _ensure_user_available(owner_username)

    parsed_layout = _parse_seed_layout(payload.seed)
    invited_friends: list[str] = []
    for friend_username in payload.invited_friends:
        normalized_friend = _normalize_username(friend_username)
        if not normalized_friend or normalized_friend == owner_username:
            continue
        _assert_joinable_friend(owner_user, normalized_friend)
        invited_friends.append(normalized_friend)

    session_id = f"mp-{uuid4().hex[:10]}"
    map_doc = maps_collection.find_one({"seed": payload.seed})
    session = {
        "session_id": session_id,
        "status": "waiting_for_guest",
        "owner_username": owner_username,
        "guest_username": None,
        "seed": payload.seed.strip(),
        "map_name": (payload.map_name or "").strip() or (map_doc.get("map_name") if map_doc else None),
        "source": (payload.source or "new").strip().lower() or "new",
        "difficulty": parsed_layout["difficulty"],
        "size": parsed_layout["size"],
        "seed_existed": map_doc is not None,
        "created_at": _utc_now_iso(),
        "started_at": None,
        "completed_at": None,
        "start_room": parsed_layout["start_room"],
        "finish_room": parsed_layout["finish_room"],
        "current_room": parsed_layout["start_room"],
        "room_lookup": parsed_layout["rooms"],
        "room_progress": {
            room_key: {"puzzle_solved": False, "reward_pickup_collected": False}
            for room_key in parsed_layout["rooms"].keys()
        },
        "room_puzzle_states": {},
        "team_gold": 0,
        "invited_friends": invited_friends,
        "move_vote": None,
        "players": {
            owner_username: _create_player_state(owner_username, "owner", parsed_layout["start_room"]),
        },
    }

    save_json(session_key(session_id), session)
    _store_user_session(owner_username, session_id)
    for invited_friend in invited_friends:
        _store_pending_invite(invited_friend, session)

    return {"status": "success", "session": _serialize_session_for_user(session, owner_username)}


@router.post("/session/invite")
@limiter.limit("30/minute")
def invite_friend_to_session(request: Request, payload: MultiplayerInvitePayload):
    username = _normalize_username(payload.username)
    friend_username = _normalize_username(payload.friend_username)
    owner_user = _load_user(username)
    _load_user(friend_username)
    _assert_joinable_friend(owner_user, friend_username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        _assert_owner(session, username)
        if session.get("guest_username"):
            raise HTTPException(status_code=409, detail="This session already has a guest and cannot accept more invites")

        invited_friends = set(session.get("invited_friends", []))
        invited_friends.add(friend_username)
        session["invited_friends"] = sorted(invited_friends, key=str.casefold)
        save_json(session_key(payload.session_id), session)
        _store_pending_invite(friend_username, session)

    _broadcast_ws_session_from_thread(session)
    return {"status": "success", "session": _serialize_session_for_user(session, username)}


@router.post("/session/join")
@limiter.limit("30/minute")
def join_multiplayer_session(request: Request, payload: MultiplayerJoinPayload):
    username = _normalize_username(payload.username)
    _load_user(username)
    _ensure_user_available(username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        if session.get("status") in {"completed", "abandoned"}:
            raise HTTPException(status_code=409, detail="This session is no longer active")

        players = session.get("players", {})
        if username in players:
            return {"status": "success", "session": _serialize_session_for_user(session, username)}

        if session.get("guest_username"):
            raise HTTPException(status_code=409, detail="This session is already full")

        if username not in set(session.get("invited_friends", [])):
            raise HTTPException(status_code=403, detail="You must be invited to join this session")

        session["guest_username"] = username
        session["status"] = "ready_check"
        session["players"][username] = _create_player_state(username, "guest", session["current_room"])
        save_json(session_key(payload.session_id), session)
        _store_user_session(username, payload.session_id)
        _clear_pending_invites(session)

    _broadcast_ws_session_from_thread(session)
    return {"status": "success", "session": _serialize_session_for_user(session, username)}


@router.post("/session/ready")
@limiter.limit("60/minute")
def set_multiplayer_ready_state(request: Request, payload: MultiplayerReadyPayload):
    username = _normalize_username(payload.username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        player = _assert_session_member(session, username)
        player["ready"] = bool(payload.ready)
        player["last_seen_at"] = _utc_now_iso()

        players = session.get("players", {})
        if len(players) == 2 and all(entry.get("ready") for entry in players.values()):
            if session.get("started_at") is None:
                session["started_at"] = _utc_now_iso()
            session["status"] = "active"
        elif len(players) == 2:
            session["status"] = "ready_check"
        else:
            session["status"] = "waiting_for_guest"

        save_json(session_key(payload.session_id), session)

    _broadcast_ws_session_from_thread(session)
    return {"status": "success", "session": _serialize_session_for_user(session, username)}


@router.get("/session")
@limiter.limit("300/minute")
def get_multiplayer_session(request: Request, session_id: str, username: str):
    normalized_username = _normalize_username(username)
    session = _load_session_or_404(session_id)
    _assert_session_member(session, normalized_username)
    return {"status": "success", "session": _serialize_session_for_user(session, normalized_username)}


@router.put("/session/state")
@limiter.limit("300/minute")
def update_multiplayer_state(request: Request, payload: MultiplayerStatePayload):
    username = _normalize_username(payload.username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        _update_multiplayer_state_locked(session, payload)
        save_json(session_key(payload.session_id), session)

    _broadcast_ws_session_from_thread(session)
    return {"status": "success", "session": _serialize_session_for_user(session, username)}


@router.post("/session/puzzle_action")
@limiter.limit("240/minute")
def multiplayer_puzzle_action(request: Request, payload: MultiplayerPuzzleActionPayload):
    username = _normalize_username(payload.username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        _apply_multiplayer_puzzle_action_locked(session, payload)
        save_json(session_key(payload.session_id), session)

    _broadcast_ws_session_from_thread(session)
    return {"status": "success", "session": _serialize_session_for_user(session, username)}


@router.post("/session/room/move")
@limiter.limit("240/minute")
def request_room_move(request: Request, payload: MultiplayerMovePayload):
    username = _normalize_username(payload.username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        room_moved = _request_room_move_locked(session, payload)
        save_json(session_key(payload.session_id), session)

    _broadcast_ws_session_from_thread(session, room_moved)
    return {
        "status": "success",
        "room_moved": room_moved,
        "session": _serialize_session_for_user(session, username),
    }


@router.post("/session/finish")
@limiter.limit("60/minute")
def finish_multiplayer_session(request: Request, payload: MultiplayerFinishPayload):
    username = _normalize_username(payload.username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        completion = _finish_multiplayer_session_locked(payload.session_id, session, username)

    _broadcast_ws_session_from_thread(session, False, completion)
    return {"status": "success", "session": _serialize_session_for_user(session, username), "completion": completion}


@router.post("/session/leave")
@limiter.limit("60/minute")
def leave_multiplayer_session(request: Request, payload: MultiplayerLeavePayload):
    username = _normalize_username(payload.username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        _leave_multiplayer_session_locked(payload.session_id, session, username, payload.reason)

    _broadcast_ws_session_from_thread(session)
    return {"status": "success", "session": _serialize_session_for_user(session, username)}


@router.post("/session/sync_saved_map")
@limiter.limit("30/minute")
def sync_saved_multiplayer_map(request: Request, payload: MultiplayerSyncSavedMapPayload):
    username = _normalize_username(payload.username)

    with session_lock(payload.session_id):
        session = _load_session_or_404(payload.session_id)
        _assert_owner(session, username)

        if session.get("status") != "completed":
            raise HTTPException(status_code=409, detail="Session must be completed before syncing map ownership")

        if session.get("source") != "new":
            raise HTTPException(status_code=409, detail="Only newly generated multiplayer maps need ownership sync")

        map_doc = maps_collection.find_one({"seed": session.get("seed")})
        if not map_doc and payload.map_name:
            map_doc = maps_collection.find_one({"map_name": payload.map_name.strip()})
        if not map_doc:
            raise HTTPException(status_code=404, detail="Saved map not found")

        guest_username = session.get("guest_username")
        users_collection.update_one(
            {"username": session.get("owner_username")},
            {"$addToSet": {"maps_owned": map_doc["_id"], "maps_discovered": map_doc["_id"]}},
        )
        if guest_username:
            users_collection.update_one(
                {"username": guest_username},
                {"$addToSet": {"maps_discovered": map_doc["_id"]}},
            )

        session["completion"] = {
            **(session.get("completion") or {}),
            "saved_map_id": str(map_doc["_id"]),
            "saved_map_name": map_doc.get("map_name"),
            "discoverers_synced": True,
        }
        save_json(session_key(payload.session_id), session, ttl_seconds=COMPLETED_SESSION_TTL_SECONDS)

    return {"status": "success", "session": _serialize_session_for_user(session, username), "completion": session.get("completion")}


@router.websocket("/session/ws/{session_id}")
async def multiplayer_session_ws(websocket: WebSocket, session_id: str, username: str):
    normalized_username = _normalize_username(username)
    await websocket.accept()

    try:
        session = _load_session_or_404(session_id)
        _assert_session_member(session, normalized_username)
    except HTTPException as exc:
        await _send_ws_error(websocket, str(exc.detail), exc.status_code)
        await websocket.close(code=1008)
        return

    _register_session_socket(session_id, normalized_username, websocket)
    await _broadcast_ws_session(session)

    try:
        while True:
            raw_message = await websocket.receive_text()
            try:
                message = json.loads(raw_message)
            except json.JSONDecodeError:
                await _send_ws_error(websocket, "Invalid multiplayer socket payload.")
                continue

            message_type = str(message.get("type") or "").strip().lower()
            room_moved = False
            completion: dict[str, Any] | None = None

            try:
                with session_lock(session_id):
                    session = _load_session_or_404(session_id)
                    _assert_session_member(session, normalized_username)

                    if message_type in {"ping", "heartbeat"}:
                        pass
                    elif message_type == "state":
                        payload = MultiplayerStatePayload.model_validate(
                            {
                                "username": normalized_username,
                                "session_id": session_id,
                                "room_x": message.get("room_x"),
                                "room_y": message.get("room_y"),
                                "position": message.get("position") or {},
                                "facing": message.get("facing", "Down"),
                                "is_on_black_hole": message.get("is_on_black_hole", False),
                                "gold_collected": message.get("gold_collected", 0),
                                "puzzle_solved": message.get("puzzle_solved", False),
                                "reward_pickup_collected": message.get("reward_pickup_collected", False),
                            }
                        )
                        _update_multiplayer_state_locked(session, payload)
                        save_json(session_key(session_id), session)
                    elif message_type == "puzzle_action":
                        payload = MultiplayerPuzzleActionPayload.model_validate(
                            {
                                "username": normalized_username,
                                "session_id": session_id,
                                "action": message.get("action"),
                                "args": message.get("args") or {},
                            }
                        )
                        _apply_multiplayer_puzzle_action_locked(session, payload)
                        save_json(session_key(session_id), session)
                    elif message_type == "room_move":
                        payload = MultiplayerMovePayload.model_validate(
                            {
                                "username": normalized_username,
                                "session_id": session_id,
                                "target_room_x": message.get("target_room_x"),
                                "target_room_y": message.get("target_room_y"),
                            }
                        )
                        room_moved = _request_room_move_locked(session, payload)
                        save_json(session_key(session_id), session)
                    elif message_type == "finish":
                        payload = MultiplayerFinishPayload.model_validate(
                            {
                                "username": normalized_username,
                                "session_id": session_id,
                            }
                        )
                        completion = _finish_multiplayer_session_locked(session_id, session, normalized_username)
                    elif message_type == "leave":
                        payload = MultiplayerLeavePayload.model_validate(
                            {
                                "username": normalized_username,
                                "session_id": session_id,
                                "reason": message.get("reason", "left_session"),
                            }
                        )
                        _leave_multiplayer_session_locked(session_id, session, normalized_username, payload.reason)
                    else:
                        raise HTTPException(status_code=400, detail="Unsupported multiplayer socket message.")
            except ValidationError as exc:
                await _send_ws_error(websocket, str(exc))
                continue
            except HTTPException as exc:
                await _send_ws_error(websocket, str(exc.detail), exc.status_code)
                continue

            if message_type in {"ping", "heartbeat"}:
                await _send_ws_payload(websocket, {"type": "pong", "status": "success"})
                continue

            await _broadcast_ws_session(session, room_moved, completion)

            if message_type in {"leave", "finish"}:
                break
    except WebSocketDisconnect:
        pass
    finally:
        for username in _unregister_session_socket(session_id, websocket):
            _schedule_socket_abandon(session_id, username)
