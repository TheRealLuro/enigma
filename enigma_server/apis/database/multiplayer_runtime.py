from __future__ import annotations

from copy import deepcopy
from datetime import datetime, timezone
from typing import Any

from fastapi import HTTPException

from .multiplayer_puzzles import CO_OP_PUZZLE_CATALOG as CO_OP_PUZZLE_CATALOG_V2

PHASE_OBSERVE = "observe"
PHASE_CONFIGURE = "configure"
PHASE_COMMIT = "commit"
PHASE_RESOLVE = "resolve"

STATUS_NOT_STARTED = "not_started"
STATUS_ACTIVE = "active"
STATUS_FAILED_TEMPORARY = "failed_temporary"
STATUS_COOLDOWN = "cooldown"
STATUS_SOLVED = "solved"

MAX_PREFLIGHT_ATTEMPTS = 6
DEFAULT_COOLDOWN = 0.8

FAMILY_BY_KEY = {
    "p": "split_signal",
    "q": "pressure_exchange",
    "r": "bridge_builder",
    "s": "mirror_minds",
    "t": "flood_control",
    "u": "cipher_relay",
    "v": "gravity_tandem",
    "w": "tidal_lock",
    "x": "strata_shift",
    "y": "echo_sync",
    "z": "temporal_weave",
}

STAGE_VISUAL_PROFILES = {
    1: "intro",
    2: "expand",
    3: "constraint",
    4: "master",
}

PROGRESS_LABELS = {
    "p": "Coherence / Synchronization",
    "w": "Route Integrity / Pressure Stability",
    "x": "Reconstruction Completeness / Archive Restoration",
}

FAILURE_LANGUAGES = {
    "p": {
        "phase_drift": {
            "failure_code": "phase_drift",
            "failure_label": "Phase Drift",
            "visual_cue": "Waveform shear detected.",
            "recovery_text": "Recenter channels and stabilize overlap.",
        },
        "stability_loss": {
            "failure_code": "stability_loss",
            "failure_label": "Stability Loss",
            "visual_cue": "Dampers flashing fault.",
            "recovery_text": "Recover stability and lock together.",
        },
        "sync_collapse": {
            "failure_code": "sync_collapse",
            "failure_label": "Sync Collapse",
            "visual_cue": "Lock ring breakup detected.",
            "recovery_text": "Reset alignment and rebuild sync.",
        },
    },
    "w": {
        "containment_leak": {
            "failure_code": "containment_leak",
            "failure_label": "Containment Leak",
            "visual_cue": "Energy bleed detected.",
            "recovery_text": "Seal the leak window and recapture.",
        },
        "pressure_breach": {
            "failure_code": "pressure_breach",
            "failure_label": "Pressure Breach",
            "visual_cue": "Overpressure pulse triggered.",
            "recovery_text": "Stabilize timing pressure before next catch.",
        },
        "routing_fault": {
            "failure_code": "routing_fault",
            "failure_label": "Routing Fault",
            "visual_cue": "Synchronization route collapsed.",
            "recovery_text": "Re-time both catches and relock.",
        },
    },
    "x": {
        "archive_conflict": {
            "failure_code": "archive_conflict",
            "failure_label": "Archive Conflict",
            "visual_cue": "Layer mismatch conflict.",
            "recovery_text": "Re-align alternating strata layers.",
        },
        "sequence_corruption": {
            "failure_code": "sequence_corruption",
            "failure_label": "Sequence Corruption",
            "visual_cue": "Temporal ordering distortion.",
            "recovery_text": "Rebuild order using stable layer anchors.",
        },
        "reconstruction_fault": {
            "failure_code": "reconstruction_fault",
            "failure_label": "Reconstruction Fault",
            "visual_cue": "Rollback marker triggered.",
            "recovery_text": "Reset reconstruction pass and retry.",
        },
    },
}


def _utc_seconds() -> float:
    return datetime.now(timezone.utc).timestamp()


def _stable_hash(value: str) -> int:
    hash_value = 2166136261
    for character in value:
        hash_value ^= ord(character)
        hash_value = (hash_value * 16777619) & 0xFFFFFFFF
    return hash_value & 0x7FFFFFFF


def _roll(seed: str, minimum: int, maximum: int) -> int:
    span = max(1, maximum - minimum + 1)
    return minimum + (_stable_hash(seed) % span)


def _clamp(value: float, minimum: float, maximum: float) -> float:
    return max(minimum, min(maximum, value))


def _room_key(session: dict[str, Any]) -> str:
    room = session.get("current_room", {})
    return f"{room.get('x')},{room.get('y')}"


def _current_room_state(session: dict[str, Any]) -> tuple[str, dict[str, Any]]:
    key = _room_key(session)
    room = session.get("room_lookup", {}).get(key)
    if not room:
        raise HTTPException(status_code=409, detail="Current multiplayer room is missing.")
    return key, room


def _role(username: str, session: dict[str, Any]) -> str:
    if str(username or "").strip() == str(session.get("owner_username") or "").strip():
        return "owner"
    if str(username or "").strip() == str(session.get("guest_username") or "").strip():
        return "guest"
    raise HTTPException(status_code=403, detail="User is not part of this co-op session.")


def _compute_stage_level(session: dict[str, Any], current_room_key: str) -> int:
    room_lookup = session.get("room_lookup", {})
    room_progress = session.get("room_progress", {})
    total_puzzle_rooms = sum(
        1
        for room in room_lookup.values()
        if str((room or {}).get("kind") or "").strip().upper() not in {"S", "F"}
    )
    solved_before_current = sum(
        1
        for key, progress in room_progress.items()
        if key != current_room_key and isinstance(progress, dict) and bool(progress.get("puzzle_solved"))
    )
    progress = solved_before_current / max(1, total_puzzle_rooms - 1)
    band = min(3, max(0, int(progress * 4)))
    return band + 1


def _progress_label_for_key(puzzle_key: str) -> str:
    return PROGRESS_LABELS.get(puzzle_key, "System Stability")


def _new_state(
    puzzle_key: str,
    difficulty: str,
    layout_seed: str,
    solution_seed: str,
    stage_level: int,
) -> dict[str, Any]:
    info = CO_OP_PUZZLE_CATALOG_V2[difficulty][puzzle_key]
    now = _utc_seconds()
    normalized_stage = min(4, max(1, int(stage_level)))
    return {
        "schema_version": 2,
        "key": puzzle_key,
        "family_id": FAMILY_BY_KEY[puzzle_key],
        "name": info["name"],
        "instruction": info["description"],
        "accent_color": info["accent_color"],
        "mechanic_type": info["mechanic_type"],
        "difficulty": difficulty,
        "layout_seed": layout_seed,
        "solution_seed": solution_seed,
        "layout_signature": "",
        "solution_signature": "",
        "phase": PHASE_OBSERVE,
        "status_code": STATUS_NOT_STARTED,
        "status_text": "Observe and coordinate.",
        "is_solved": False,
        "completed": False,
        "can_interact": True,
        "progress": 0.0,
        "progress_label": _progress_label_for_key(puzzle_key),
        "progress_value": 0.0,
        "progress_trend": "steady",
        "attempt": 1,
        "last_tick": now,
        "cooldown_until": 0.0,
        "pending_reset": False,
        "failure_code": "",
        "failure_label": "",
        "failure_visual_cue": "",
        "recovery_text": "",
        "stage_level": normalized_stage,
        "stage_visual_profile": STAGE_VISUAL_PROFILES.get(normalized_stage, "master"),
        "runtime": {},
        "reset_runtime": {},
        "solution_script": [],
        "preflight_trace": [],
        "preflight_validated": False,
    }


def _set_status(state: dict[str, Any], phase: str, status_code: str, text: str, can_interact: bool) -> None:
    state["phase"] = phase
    state["status_code"] = status_code
    state["status_text"] = text
    state["can_interact"] = can_interact


def _resolve_failure_payload(state: dict[str, Any], failure_code: str | None) -> dict[str, str]:
    family_language = FAILURE_LANGUAGES.get(state["key"], {})
    if failure_code and failure_code in family_language:
        return family_language[failure_code]
    if family_language:
        return next(iter(family_language.values()))
    return {
        "failure_code": failure_code or "system_fault",
        "failure_label": "System Fault",
        "visual_cue": "Control signal unstable.",
        "recovery_text": "Reset and retry with stable inputs.",
    }


def _apply_failure_payload(state: dict[str, Any], failure_code: str | None) -> dict[str, str]:
    payload = _resolve_failure_payload(state, failure_code)
    state["failure_code"] = payload["failure_code"]
    state["failure_label"] = payload["failure_label"]
    state["failure_visual_cue"] = payload["visual_cue"]
    state["recovery_text"] = payload["recovery_text"]
    return payload


def _clear_failure_payload(state: dict[str, Any]) -> None:
    state["failure_code"] = ""
    state["failure_label"] = ""
    state["failure_visual_cue"] = ""
    state["recovery_text"] = ""


def _mark_solved(state: dict[str, Any], text: str) -> None:
    state["is_solved"] = True
    state["completed"] = True
    state["progress"] = 1.0
    state["progress_value"] = 1.0
    state["progress_trend"] = "up"
    _clear_failure_payload(state)
    _set_status(state, PHASE_RESOLVE, STATUS_SOLVED, text, False)


def _queue_failure(state: dict[str, Any], now: float, text: str, failure_code: str | None = None) -> None:
    payload = _apply_failure_payload(state, failure_code)
    state["pending_reset"] = True
    state["cooldown_until"] = now + DEFAULT_COOLDOWN
    status_text = text if text else f"{payload['failure_label']}: {payload['visual_cue']}"
    _set_status(state, PHASE_RESOLVE, STATUS_FAILED_TEMPORARY, status_text, False)


def _reset_after_failure(state: dict[str, Any]) -> None:
    state["runtime"] = deepcopy(state["reset_runtime"])
    state["attempt"] = int(state.get("attempt", 1)) + 1
    state["pending_reset"] = False
    state["cooldown_until"] = 0.0
    recovery = str(state.get("recovery_text") or "").strip()
    status_text = f"{recovery} Reconfigure."
    _clear_failure_payload(state)
    _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, status_text if recovery else "Reset complete. Reconfigure.", True)


def _phase_value(now: float, speed: float, offset: float) -> float:
    value = (now * speed) + offset
    return value - int(value)


def _phase_distance(a: float, b: float) -> float:
    delta = abs(a - b)
    return min(delta, 1.0 - delta)


def _next_phase_delay(now: float, speed: float, offset: float, target: float, minimum: float = 0.05) -> float:
    current = _phase_value(now, speed, offset)
    raw = (target - current) % 1.0
    delay = raw / max(0.001, speed)
    if delay < minimum:
        delay += 1.0 / max(0.001, speed)
    return delay


def _mk_action(cmd: str, label: str, icon: str, tone: str, enabled: bool, active: bool = False) -> dict[str, Any]:
    return {"cmd": cmd, "label": label, "icon": icon, "tone": tone, "enabled": bool(enabled), "active": bool(active)}


def _mk_hud(label: str, value: str, icon: str, tone: str = "shared") -> dict[str, Any]:
    return {"label": label, "value": value, "icon": icon, "tone": tone}


def _gen_state(
    puzzle_key: str,
    difficulty: str,
    layout_seed: str,
    solution_seed: str,
    stage_level: int,
) -> dict[str, Any]:
    state = _new_state(puzzle_key, difficulty, layout_seed, solution_seed, stage_level)
    script: list[dict[str, Any]] = []

    if puzzle_key in {"p", "u", "v"}:
        max_a = 9 if puzzle_key != "u" else 25
        max_b = 9 if puzzle_key == "p" else 5 if puzzle_key == "v" else 3
        if puzzle_key == "p" and stage_level == 1:
            max_a = 6
            max_b = 6
        start_a = _roll(f"{layout_seed}|a_start", 0, max_a)
        start_b = _roll(f"{layout_seed}|b_start", 0, max_b)
        target_a = _roll(f"{solution_seed}|a_target", 1, max_a)
        target_b = _roll(f"{solution_seed}|b_target", 1, max_b)
        runtime = {
            "a": start_a,
            "b": start_b,
            "target_a": target_a,
            "target_b": target_b,
            "owner_lock": False,
            "guest_lock": False,
        }
        while runtime["a"] < target_a:
            script.append({"role": "owner", "cmd": "owner_up"})
            runtime["a"] += 1
        while runtime["a"] > target_a:
            script.append({"role": "owner", "cmd": "owner_down"})
            runtime["a"] -= 1
        while runtime["b"] < target_b:
            script.append({"role": "guest", "cmd": "guest_up"})
            runtime["b"] += 1
        while runtime["b"] > target_b:
            script.append({"role": "guest", "cmd": "guest_down"})
            runtime["b"] -= 1
        if puzzle_key == "v":
            script.extend([{"role": "owner", "cmd": "owner_arm"}, {"role": "guest", "cmd": "guest_arm"}, {"role": "guest", "cmd": "launch"}])
        else:
            script.extend([{"role": "owner", "cmd": "owner_lock"}, {"role": "guest", "cmd": "guest_lock"}])
        state["runtime"] = {"a": start_a, "b": start_b, "target_a": target_a, "target_b": target_b, "owner_lock": False, "guest_lock": False}
        state["layout_signature"] = f"{state['family_id']}:vector"
        state["solution_signature"] = f"{target_a}:{target_b}:{max_a}:{max_b}"

    elif puzzle_key == "q":
        count = 3 if difficulty == "easy" else 4 if difficulty == "medium" else 5
        pressures = [_roll(f"{layout_seed}|p_start|{idx}", 0, 2) for idx in range(count)]
        target = [_roll(f"{solution_seed}|p_target|{idx}", 3, 7) for idx in range(count)]
        for idx in range(count):
            delta = target[idx] - pressures[idx]
            for _ in range(max(0, delta)):
                script.append({"role": "owner", "cmd": f"owner_raise_{idx}"})
            for _ in range(max(0, -delta)):
                script.append({"role": "guest", "cmd": f"guest_vent_{idx}"})
        script.extend([{"role": "owner", "cmd": "owner_lock"}, {"role": "guest", "cmd": "guest_lock"}])
        state["runtime"] = {"pressures": pressures, "target": target, "owner_lock": False, "guest_lock": False, "drift_acc": 0.0}
        state["layout_signature"] = f"pressure_exchange:{count}"
        state["solution_signature"] = ":".join(str(v) for v in target)

    elif puzzle_key == "r":
        count = 6 if difficulty == "easy" else 8 if difficulty == "medium" else 10
        required = [bool(_roll(f"{solution_seed}|required|{idx}", 0, 1)) for idx in range(count)]
        active = [False for _ in range(count)]
        owner = [idx for idx in range(count) if idx % 2 == 0]
        guest = [idx for idx in range(count) if idx % 2 == 1]
        if not any(required[idx] for idx in owner):
            required[owner[0]] = True
        if not any(required[idx] for idx in guest):
            required[guest[0]] = True
        for idx in range(count):
            if required[idx]:
                script.append({"role": "owner" if idx in owner else "guest", "cmd": f"toggle_{idx}"})
        state["runtime"] = {"active": active, "required": required, "owner": owner, "guest": guest}
        state["layout_signature"] = f"bridge_builder:{count}"
        state["solution_signature"] = "".join("1" if v else "0" for v in required)

    elif puzzle_key == "s":
        cells = 4 if difficulty == "easy" else 6 if difficulty == "medium" else 8
        owner_target = [_roll(f"{solution_seed}|o|{idx}", 0, 3) for idx in range(cells)]
        guest_target = [_roll(f"{solution_seed}|g|{idx}", 0, 3) for idx in range(cells)]
        for idx in range(cells):
            for _ in range(owner_target[idx]):
                script.append({"role": "owner", "cmd": f"owner_cycle_{idx}"})
            for _ in range(guest_target[idx]):
                script.append({"role": "guest", "cmd": f"guest_cycle_{idx}"})
        state["runtime"] = {"owner_values": [0] * cells, "guest_values": [0] * cells, "owner_target": owner_target, "guest_target": guest_target}
        state["layout_signature"] = f"mirror_minds:{cells}"
        state["solution_signature"] = f"{sum(owner_target)}:{sum(guest_target)}"

    elif puzzle_key == "t":
        count = 3 if difficulty == "easy" else 4 if difficulty == "medium" else 5
        water = [_roll(f"{layout_seed}|w_start|{idx}", 4, 8) for idx in range(count)]
        safe = [_roll(f"{solution_seed}|safe|{idx}", 2, 4) for idx in range(count)]
        for idx in range(count):
            script.append({"role": "owner", "cmd": f"gate_{idx}"})
            for _ in range(max(0, water[idx] - safe[idx])):
                script.append({"role": "guest", "cmd": f"pump_{idx}"})
        script.extend([{"role": "owner", "cmd": "owner_lock"}, {"role": "guest", "cmd": "guest_lock"}, {"role": "owner", "cmd": "wait", "delay": 1.0}])
        state["runtime"] = {"water": [float(v) for v in water], "safe": safe, "gates": [False] * count, "owner_lock": False, "guest_lock": False, "hold": 0.0}
        state["layout_signature"] = f"flood_control:{count}"
        state["solution_signature"] = ":".join(str(v) for v in safe)

    elif puzzle_key == "w":
        if stage_level == 1:
            speed = 0.22
            required = 1
        else:
            speed = 0.23 if difficulty == "easy" else 0.30 if difficulty == "medium" else 0.37
            required = 2 if difficulty == "easy" else 3 if difficulty == "medium" else 4
        target_phase = _roll(f"{solution_seed}|phase", 140, 860) / 1000.0
        offset = _roll(f"{layout_seed}|offset", 0, 1000) / 1000.0
        if stage_level == 1:
            sync_tol = 0.42
            phase_tol = 0.16
        else:
            sync_tol = 0.34 if difficulty == "easy" else 0.25 if difficulty == "medium" else 0.18
            phase_tol = 0.12 if difficulty == "easy" else 0.09 if difficulty == "medium" else 0.06
        now = float(state["last_tick"])
        for _ in range(required):
            delay = _next_phase_delay(now, speed, offset, target_phase, 0.05)
            script.append({"role": "owner", "cmd": "wait", "delay": delay})
            now += delay
            script.append({"role": "owner", "cmd": "catch"})
            script.append({"role": "guest", "cmd": "wait", "delay": max(0.02, sync_tol * 0.35)})
            now += max(0.02, sync_tol * 0.35)
            script.append({"role": "guest", "cmd": "catch"})
        state["runtime"] = {
            "speed": speed,
            "target_phase": target_phase,
            "offset": offset,
            "required": required,
            "sync_tol": sync_tol,
            "phase_tol": phase_tol,
            "owner_catch": None,
            "guest_catch": None,
            "catches": 0,
        }
        state["layout_signature"] = f"tidal_lock:{speed:.3f}"
        state["solution_signature"] = f"{target_phase:.3f}:{required}:{sync_tol:.2f}:{phase_tol:.2f}"

    elif puzzle_key == "x":
        count = (
            4
            if stage_level == 1
            else 6 if difficulty == "easy" else 8 if difficulty == "medium" else 10
        )
        layers = [_roll(f"{layout_seed}|layer|{idx}", -1, 1) for idx in range(count)]
        target = [_roll(f"{solution_seed}|target|{idx}", -2, 2) for idx in range(count)]
        owner = [idx for idx in range(count) if idx % 2 == 0]
        guest = [idx for idx in range(count) if idx % 2 == 1]
        for idx in range(count):
            role = "owner" if idx in owner else "guest"
            current = layers[idx]
            while current < target[idx]:
                script.append({"role": role, "cmd": f"shift_{idx}_up"})
                current += 1
            while current > target[idx]:
                script.append({"role": role, "cmd": f"shift_{idx}_down"})
                current -= 1
        script.extend([{"role": "owner", "cmd": "owner_lock"}, {"role": "guest", "cmd": "guest_lock"}])
        state["runtime"] = {
            "layers": layers,
            "target": target,
            "owner": owner,
            "guest": guest,
            "owner_lock": False,
            "guest_lock": False,
        }
        state["layout_signature"] = f"strata_shift:{count}"
        state["solution_signature"] = ":".join(str(v) for v in target)

    elif puzzle_key == "y":
        speed = 0.31 if difficulty == "easy" else 0.39 if difficulty == "medium" else 0.46
        required = 2 if difficulty == "easy" else 3 if difficulty == "medium" else 4
        target_phase = _roll(f"{solution_seed}|phase", 80, 920) / 1000.0
        offset = _roll(f"{layout_seed}|offset", 0, 1000) / 1000.0
        freq = _roll(f"{layout_seed}|freq", 120, 420)
        target_freq = _roll(f"{solution_seed}|target_freq", 180, 540)
        step = 20
        while freq < target_freq:
            script.append({"role": "guest", "cmd": "guest_freq_up"})
            freq += step
        while freq > target_freq:
            script.append({"role": "guest", "cmd": "guest_freq_down"})
            freq -= step
        now = float(state["last_tick"])
        for _ in range(required):
            delay = _next_phase_delay(now, speed, offset, target_phase, 0.45)
            script.append({"role": "owner", "cmd": "wait", "delay": delay})
            now += delay
            script.append({"role": "owner", "cmd": "fire"})
        state["runtime"] = {
            "speed": speed,
            "target_phase": target_phase,
            "offset": offset,
            "freq": _roll(f"{layout_seed}|freq", 120, 420),
            "target_freq": target_freq,
            "step": step,
            "required": required,
            "res": 0,
            "last_fire": 0.0,
        }
        state["layout_signature"] = f"echo_sync:{speed:.3f}"
        state["solution_signature"] = f"{target_phase:.3f}:{target_freq}:{required}"

    elif puzzle_key == "z":
        rows = 2 if difficulty == "easy" else 3 if difficulty == "medium" else 4
        cols = 3 if difficulty == "easy" else 4 if difficulty == "medium" else 5
        past_target = [[bool(_roll(f"{solution_seed}|past|{r}|{c}", 0, 1)) for c in range(cols)] for r in range(rows)]
        present_target = [[bool(_roll(f"{solution_seed}|present|{r}|{c}", 0, 1)) for c in range(cols)] for r in range(rows)]
        links = []
        for idx in range(2 if difficulty == "easy" else 4 if difficulty == "medium" else 7):
            pr = _roll(f"{layout_seed}|lpr|{idx}", 0, rows - 1)
            pc = _roll(f"{layout_seed}|lpc|{idx}", 0, cols - 1)
            rr = _roll(f"{layout_seed}|lrr|{idx}", 0, rows - 1)
            rc = _roll(f"{layout_seed}|lrc|{idx}", 0, cols - 1)
            links.append({"past": [pr, pc], "present": [rr, rc]})
            linked = past_target[pr][pc] or present_target[rr][rc]
            past_target[pr][pc] = linked
            present_target[rr][rc] = linked
        for r in range(rows):
            for c in range(cols):
                if past_target[r][c]:
                    script.append({"role": "owner", "cmd": f"past_{r}_{c}"})
        for r in range(rows):
            for c in range(cols):
                if present_target[r][c]:
                    script.append({"role": "guest", "cmd": f"present_{r}_{c}"})
        state["runtime"] = {
            "rows": rows,
            "cols": cols,
            "past": [[False for _ in range(cols)] for _ in range(rows)],
            "present": [[False for _ in range(cols)] for _ in range(rows)],
            "past_target": past_target,
            "present_target": present_target,
            "links": links,
            "past_clues": [sum(1 for value in row if value) for row in past_target],
            "present_clues": [sum(1 for value in row if value) for row in present_target],
        }
        state["layout_signature"] = f"temporal_weave:{rows}x{cols}:{len(links)}"
        state["solution_signature"] = f"{sum(state['runtime']['past_clues'])}:{sum(state['runtime']['present_clues'])}"

    state["solution_signature"] = f"{state['solution_signature']}|{_stable_hash(solution_seed)}"
    state["solution_script"] = script
    state["reset_runtime"] = deepcopy(state["runtime"])
    _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Use your controls and commit together.", True)
    return state



def _command_allowed(state: dict[str, Any], role: str, cmd: str) -> bool:
    key = state["key"]
    runtime = state["runtime"]
    if cmd == "wait":
        return True
    if key in {"p", "u", "v"}:
        if cmd.startswith("owner_"):
            return role == "owner"
        if cmd.startswith("guest_") or cmd == "launch":
            return role == "guest"
        return False
    if key == "q":
        return (cmd.startswith("owner_") and role == "owner") or (cmd.startswith("guest_") and role == "guest")
    if key == "r":
        if not cmd.startswith("toggle_"):
            return False
        index = int(cmd.split("_")[1])
        return index in set(runtime["owner"]) if role == "owner" else index in set(runtime["guest"])
    if key == "s":
        return (cmd.startswith("owner_") and role == "owner") or (cmd.startswith("guest_") and role == "guest")
    if key == "t":
        return (cmd.startswith("gate_") and role == "owner") or (cmd in {"owner_lock"} and role == "owner") or (cmd.startswith("pump_") and role == "guest") or (cmd in {"guest_lock"} and role == "guest")
    if key == "w":
        return cmd == "catch"
    if key == "x":
        if cmd == "owner_lock":
            return role == "owner"
        if cmd == "guest_lock":
            return role == "guest"
        if not cmd.startswith("shift_"):
            return False
        layer = int(cmd.split("_")[1])
        return layer in set(runtime["owner"]) if role == "owner" else layer in set(runtime["guest"])
    if key == "y":
        return (cmd.startswith("guest_") and role == "guest") or (cmd == "fire" and role == "owner")
    if key == "z":
        return (cmd.startswith("past_") and role == "owner") or (cmd.startswith("present_") and role == "guest")
    return False


def _apply_cmd(state: dict[str, Any], role: str, cmd: str, now: float) -> None:
    key = state["key"]
    runtime = state["runtime"]
    if cmd == "wait":
        return

    if key in {"p", "u", "v"}:
        max_a = 9 if key != "u" else 25
        max_b = 9 if key == "p" else 5 if key == "v" else 3
        if cmd == "owner_up":
            runtime["a"] = int(_clamp(int(runtime["a"]) + 1, 0, max_a))
            runtime["owner_lock"] = False
        elif cmd == "owner_down":
            runtime["a"] = int(_clamp(int(runtime["a"]) - 1, 0, max_a))
            runtime["owner_lock"] = False
        elif cmd == "guest_up":
            runtime["b"] = int(_clamp(int(runtime["b"]) + 1, 0, max_b))
            runtime["guest_lock"] = False
        elif cmd == "guest_down":
            runtime["b"] = int(_clamp(int(runtime["b"]) - 1, 0, max_b))
            runtime["guest_lock"] = False
        elif cmd == "owner_lock":
            runtime["owner_lock"] = True
        elif cmd == "guest_lock":
            runtime["guest_lock"] = True
        elif cmd == "owner_arm":
            runtime["owner_lock"] = True
        elif cmd == "guest_arm":
            runtime["guest_lock"] = True
        elif cmd == "launch":
            if not runtime["owner_lock"] or not runtime["guest_lock"]:
                _set_status(state, PHASE_COMMIT, STATUS_ACTIVE, "Both players must arm before launch.", True)
                return
            if int(runtime["a"]) == int(runtime["target_a"]) and int(runtime["b"]) == int(runtime["target_b"]):
                _mark_solved(state, "Trajectory converged.")
            else:
                _queue_failure(state, now, "Launch diverged. Resetting.", "sync_collapse" if key == "p" else None)
            return
        else:
            raise HTTPException(status_code=400, detail="Invalid puzzle command.")
        if runtime["owner_lock"] and runtime["guest_lock"]:
            if int(runtime["a"]) == int(runtime["target_a"]) and int(runtime["b"]) == int(runtime["target_b"]):
                _mark_solved(state, "Dual channels locked.")
            else:
                _queue_failure(state, now, "Lock mismatch. Reconfigure.", "phase_drift" if key == "p" else None)
        else:
            _set_status(state, PHASE_COMMIT, STATUS_ACTIVE, "Align both channels and lock.", True)
        return

    if key == "q":
        if cmd.startswith("owner_raise_"):
            idx = int(cmd.split("_")[-1])
            runtime["pressures"][idx] = int(_clamp(int(runtime["pressures"][idx]) + 1, 0, 9))
            runtime["owner_lock"] = False
        elif cmd.startswith("guest_vent_"):
            idx = int(cmd.split("_")[-1])
            runtime["pressures"][idx] = int(_clamp(int(runtime["pressures"][idx]) - 1, 0, 9))
            runtime["guest_lock"] = False
        elif cmd == "owner_lock":
            runtime["owner_lock"] = True
        elif cmd == "guest_lock":
            runtime["guest_lock"] = True
        else:
            raise HTTPException(status_code=400, detail="Invalid pressure command.")
        if runtime["owner_lock"] and runtime["guest_lock"]:
            if runtime["pressures"] == runtime["target"]:
                _mark_solved(state, "Pressure profile stabilized.")
            else:
                _queue_failure(state, now, "Pressure commit failed.")
        else:
            _set_status(state, PHASE_COMMIT, STATUS_ACTIVE, "Raise/vent channels and commit together.", True)
        return

    if key == "r":
        idx = int(cmd.split("_")[1])
        runtime["active"][idx] = not bool(runtime["active"][idx])
        if all(bool(runtime["active"][i]) == bool(runtime["required"][i]) for i in range(len(runtime["required"]))):
            _mark_solved(state, "Bridge stabilized.")
        else:
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Toggle segments to complete the path.", True)
        return

    if key == "s":
        if cmd.startswith("owner_cycle_"):
            idx = int(cmd.split("_")[-1])
            runtime["owner_values"][idx] = (int(runtime["owner_values"][idx]) + 1) % 4
        elif cmd.startswith("guest_cycle_"):
            idx = int(cmd.split("_")[-1])
            runtime["guest_values"][idx] = (int(runtime["guest_values"][idx]) + 1) % 4
        else:
            raise HTTPException(status_code=400, detail="Invalid mirror command.")
        if runtime["owner_values"] == runtime["owner_target"] and runtime["guest_values"] == runtime["guest_target"]:
            _mark_solved(state, "Mirror arrays synchronized.")
        else:
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Cycle cells to match your mirrored pattern.", True)
        return

    if key == "t":
        if cmd.startswith("gate_"):
            idx = int(cmd.split("_")[-1])
            runtime["gates"][idx] = not bool(runtime["gates"][idx])
            runtime["owner_lock"] = False
        elif cmd.startswith("pump_"):
            idx = int(cmd.split("_")[-1])
            runtime["water"][idx] = _clamp(float(runtime["water"][idx]) - 1.0, 0.0, 9.0)
            runtime["guest_lock"] = False
        elif cmd == "owner_lock":
            runtime["owner_lock"] = True
        elif cmd == "guest_lock":
            runtime["guest_lock"] = True
        else:
            raise HTTPException(status_code=400, detail="Invalid flood command.")
        if runtime["owner_lock"] and runtime["guest_lock"]:
            if all(float(runtime["water"][idx]) <= int(runtime["safe"][idx]) for idx in range(len(runtime["safe"]))):
                _set_status(state, PHASE_COMMIT, STATUS_ACTIVE, "Safe profile reached. Hold it.", True)
            else:
                _queue_failure(state, now, "Unsafe flood commit.")
        else:
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Set gates/pumps, then lock together.", True)
        return
    if key == "w":
        phase = _phase_value(now, float(runtime["speed"]), float(runtime["offset"]))
        runtime[f"{role}_catch"] = {"t": now, "p": phase}
        owner = runtime.get("owner_catch")
        guest = runtime.get("guest_catch")
        if owner and guest:
            dt = abs(float(owner["t"]) - float(guest["t"]))
            owner_d = _phase_distance(float(owner["p"]), float(runtime["target_phase"]))
            guest_d = _phase_distance(float(guest["p"]), float(runtime["target_phase"]))
            runtime["owner_catch"] = None
            runtime["guest_catch"] = None
            if dt <= float(runtime["sync_tol"]) and owner_d <= float(runtime["phase_tol"]) and guest_d <= float(runtime["phase_tol"]):
                runtime["catches"] = int(runtime["catches"]) + 1
                if int(runtime["catches"]) >= int(runtime["required"]):
                    _mark_solved(state, "Tidal lock captured.")
                else:
                    _set_status(state, PHASE_COMMIT, STATUS_ACTIVE, "Catch confirmed. Repeat on next cycle.", True)
            else:
                failure_code = "routing_fault" if dt > float(runtime["sync_tol"]) else "pressure_breach"
                _queue_failure(state, now, "Window miss. Tidal lock reset.", failure_code)
        else:
            _set_status(state, PHASE_COMMIT, STATUS_ACTIVE, "Catch armed. Waiting for partner.", True)
        return

    if key == "x":
        if cmd == "owner_lock":
            runtime["owner_lock"] = True
        elif cmd == "guest_lock":
            runtime["guest_lock"] = True
        else:
            parts = cmd.split("_")
            idx = int(parts[1])
            delta = 1 if parts[2] == "up" else -1
            runtime["layers"][idx] = int(_clamp(int(runtime["layers"][idx]) + delta, -2, 2))
            runtime["owner_lock"] = False
            runtime["guest_lock"] = False

        if runtime.get("owner_lock") and runtime.get("guest_lock"):
            if runtime["layers"] == runtime["target"]:
                _mark_solved(state, "Strata aligned.")
            else:
                failure_code = "archive_conflict" if int(state.get("stage_level", 1)) <= 2 else "sequence_corruption"
                _queue_failure(state, now, "Layer commit conflict detected.", failure_code)
        else:
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Shift alternating layers to the target.", True)
        return

    if key == "y":
        if cmd == "guest_freq_up":
            runtime["freq"] = int(_clamp(int(runtime["freq"]) + int(runtime["step"]), 100, 1000))
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Frequency tuned.", True)
            return
        if cmd == "guest_freq_down":
            runtime["freq"] = int(_clamp(int(runtime["freq"]) - int(runtime["step"]), 100, 1000))
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Frequency tuned.", True)
            return
        if cmd != "fire":
            raise HTTPException(status_code=400, detail="Invalid echo command.")
        if runtime["last_fire"] and (now - float(runtime["last_fire"])) < 0.38:
            raise HTTPException(status_code=409, detail="Pulse emitter is cooling down.")
        runtime["last_fire"] = now
        phase = _phase_value(now, float(runtime["speed"]), float(runtime["offset"]))
        phase_ok = _phase_distance(phase, float(runtime["target_phase"])) <= (0.11 if state["difficulty"] == "easy" else 0.08 if state["difficulty"] == "medium" else 0.06)
        freq_ok = abs(int(runtime["freq"]) - int(runtime["target_freq"])) <= (30 if state["difficulty"] == "easy" else 20 if state["difficulty"] == "medium" else 10)
        if phase_ok and freq_ok:
            runtime["res"] = int(runtime["res"]) + 1
            if int(runtime["res"]) >= int(runtime["required"]):
                _mark_solved(state, "Echo resonance synchronized.")
            else:
                _set_status(state, PHASE_COMMIT, STATUS_ACTIVE, "Resonance locked. Chain the next pulse.", True)
        else:
            runtime["res"] = 0
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Resonance lost. Re-sync.", True)
        return

    if key == "z":
        parts = cmd.split("_")
        source = parts[0]
        row = int(parts[1])
        col = int(parts[2])
        if source == "past":
            runtime["past"][row][col] = True
            for link in runtime["links"]:
                if link["past"] == [row, col]:
                    runtime["present"][int(link["present"][0])][int(link["present"][1])] = True
        elif source == "present":
            runtime["present"][row][col] = True
            for link in runtime["links"]:
                if link["present"] == [row, col]:
                    runtime["past"][int(link["past"][0])][int(link["past"][1])] = True
        else:
            raise HTTPException(status_code=400, detail="Invalid temporal command.")
        for r in range(int(runtime["rows"])):
            if sum(1 for val in runtime["past"][r] if val) > int(runtime["past_clues"][r]):
                _queue_failure(state, now, "Temporal paradox detected.")
                return
            if sum(1 for val in runtime["present"][r] if val) > int(runtime["present_clues"][r]):
                _queue_failure(state, now, "Temporal paradox detected.")
                return
        if runtime["past"] == runtime["past_target"] and runtime["present"] == runtime["present_target"]:
            _mark_solved(state, "Temporal weave stabilized.")
        else:
            _set_status(state, PHASE_CONFIGURE, STATUS_ACTIVE, "Fill both timelines without paradox.", True)


def _refresh_progress(state: dict[str, Any]) -> None:
    previous = float(state.get("progress_value", state.get("progress", 0.0)) or 0.0)
    if state["is_solved"]:
        progress = 1.0
    else:
        runtime = state["runtime"]
        key = state["key"]
        if key in {"p", "u", "v"}:
            a_score = 1.0 - min(1.0, abs(int(runtime["a"]) - int(runtime["target_a"])) / max(1, 25 if key == "u" else 9))
            b_score = 1.0 - min(1.0, abs(int(runtime["b"]) - int(runtime["target_b"])) / max(1, 9 if key == "p" else 5 if key == "v" else 3))
            lock_bonus = (1 if runtime["owner_lock"] else 0) + (1 if runtime["guest_lock"] else 0)
            progress = _clamp((a_score + b_score + (lock_bonus / 2.0)) / 3.0, 0.0, 0.99)
        elif key == "q":
            score = 0.0
            for idx in range(len(runtime["target"])):
                score += 1.0 - min(1.0, abs(int(runtime["pressures"][idx]) - int(runtime["target"][idx])) / 9.0)
            score /= max(1, len(runtime["target"]))
            lock_bonus = (1 if runtime["owner_lock"] else 0) + (1 if runtime["guest_lock"] else 0)
            progress = _clamp((score + (lock_bonus / 2.0)) / 2.0, 0.0, 0.99)
        elif key == "r":
            matched = sum(1 for i in range(len(runtime["required"])) if bool(runtime["active"][i]) == bool(runtime["required"][i]))
            progress = _clamp(matched / max(1, len(runtime["required"])), 0.0, 0.99)
        elif key == "s":
            matched = sum(1 for i in range(len(runtime["owner_target"])) if int(runtime["owner_values"][i]) == int(runtime["owner_target"][i]))
            matched += sum(1 for i in range(len(runtime["guest_target"])) if int(runtime["guest_values"][i]) == int(runtime["guest_target"][i]))
            progress = _clamp(matched / max(1, len(runtime["owner_target"]) * 2), 0.0, 0.99)
        elif key == "t":
            score = sum(1 for i in range(len(runtime["safe"])) if float(runtime["water"][i]) <= int(runtime["safe"][i]))
            hold = min(1.0, float(runtime["hold"]) / 1.0)
            progress = _clamp(((score / max(1, len(runtime["safe"]))) + hold) / 2.0, 0.0, 0.99)
        elif key == "w":
            progress = _clamp(int(runtime["catches"]) / max(1, int(runtime["required"])), 0.0, 0.99)
        elif key == "x":
            matched = sum(1 for i in range(len(runtime["target"])) if int(runtime["layers"][i]) == int(runtime["target"][i]))
            lock_bonus = (1 if runtime.get("owner_lock") else 0) + (1 if runtime.get("guest_lock") else 0)
            progress = _clamp(((matched / max(1, len(runtime["target"]))) + (lock_bonus / 2.0)) / 2.0, 0.0, 0.99)
        elif key == "y":
            res = _clamp(int(runtime["res"]) / max(1, int(runtime["required"])), 0.0, 0.99)
            freq = 1.0 - min(1.0, abs(int(runtime["freq"]) - int(runtime["target_freq"])) / 500.0)
            progress = _clamp((res + freq) / 2.0, 0.0, 0.99)
        elif key == "z":
            rows = int(runtime["rows"])
            cols = int(runtime["cols"])
            total = max(1, rows * cols * 2)
            matched = 0
            for r in range(rows):
                for c in range(cols):
                    if bool(runtime["past"][r][c]) == bool(runtime["past_target"][r][c]):
                        matched += 1
                    if bool(runtime["present"][r][c]) == bool(runtime["present_target"][r][c]):
                        matched += 1
            progress = _clamp(matched / total, 0.0, 0.99)
        else:
            progress = _clamp(float(state.get("progress", 0.0) or 0.0), 0.0, 0.99)

    state["progress"] = progress
    state["progress_value"] = progress
    delta = progress - previous
    if delta > 0.001:
        state["progress_trend"] = "up"
    elif delta < -0.001:
        state["progress_trend"] = "down"
    else:
        state["progress_trend"] = "steady"


def tick_puzzle_state_v2(state: dict[str, Any], now: float, delta: float | None = None) -> None:
    if state["is_solved"]:
        return
    prev = float(state.get("last_tick", now))
    elapsed = max(0.0, now - prev) if delta is None else max(0.0, delta)
    state["last_tick"] = now
    if state.get("pending_reset"):
        if now < float(state.get("cooldown_until", 0.0)):
            _set_status(state, PHASE_RESOLVE, STATUS_COOLDOWN, state["status_text"], False)
            _refresh_progress(state)
            return
        _reset_after_failure(state)

    if state["key"] == "t":
        runtime = state["runtime"]
        locked_safe = runtime["owner_lock"] and runtime["guest_lock"] and all(float(runtime["water"][i]) <= int(runtime["safe"][i]) for i in range(len(runtime["safe"])))
        if not locked_safe:
            for i in range(len(runtime["water"])):
                rise = 1.25 * elapsed
                if runtime["gates"][i]:
                    rise *= 0.25
                runtime["water"][i] = _clamp(float(runtime["water"][i]) + rise, 0.0, 9.0)
        if any(float(v) >= 8.95 for v in runtime["water"]):
            _queue_failure(state, now, "Flood breach detected.")
        if locked_safe:
            runtime["hold"] = float(runtime["hold"]) + elapsed
            if runtime["hold"] >= 1.0:
                _mark_solved(state, "Flood channels stabilized.")
        else:
            runtime["hold"] = 0.0
    elif state["key"] == "w":
        runtime = state["runtime"]
        for side in ("owner_catch", "guest_catch"):
            entry = runtime.get(side)
            if entry and (now - float(entry["t"])) > 1.0:
                runtime[side] = None
    elif state["key"] == "y":
        runtime = state["runtime"]
        if runtime["res"] > 0 and runtime["last_fire"] and (now - float(runtime["last_fire"])) > 4.0:
            runtime["res"] = max(0, int(runtime["res"]) - 1)
            state["status_text"] = "Resonance chain fading."
    _refresh_progress(state)


def _simulate_trace(state: dict[str, Any], trace: list[dict[str, Any]]) -> bool:
    sim = deepcopy(state)
    now = float(sim.get("last_tick", _utc_seconds()))
    for step in trace:
        cmd = str(step.get("cmd") or "").strip()
        role = str(step.get("role") or "").strip()
        delay = float(step.get("delay", 0.0) or 0.0)
        if delay > 0:
            now += delay
            tick_puzzle_state_v2(sim, now, delay)
        if cmd == "wait":
            continue
        if not _command_allowed(sim, role, cmd):
            return False
        _apply_cmd(sim, role, cmd, now)
        tick_puzzle_state_v2(sim, now, 0.0)
        if sim["is_solved"]:
            break
    return bool(sim["is_solved"])


def try_solve_v2(state: dict[str, Any]) -> list[dict[str, Any]] | None:
    trace = deepcopy(state.get("solution_script", []))
    if not trace:
        return None
    baseline = deepcopy(state)
    if isinstance(baseline.get("reset_runtime"), dict) and baseline.get("reset_runtime"):
        baseline["runtime"] = deepcopy(baseline["reset_runtime"])
    baseline["is_solved"] = False
    baseline["completed"] = False
    baseline["pending_reset"] = False
    baseline["cooldown_until"] = 0.0
    return trace if _simulate_trace(baseline, trace) else None


def _create_state_with_preflight(session: dict[str, Any], room_key: str, room: dict[str, Any]) -> dict[str, Any]:
    difficulty = str(session.get("difficulty") or "easy").strip().lower()
    if difficulty not in CO_OP_PUZZLE_CATALOG_V2:
        difficulty = "easy"
    puzzle_key = str(room.get("puzzle_key") or "").strip().lower()
    if puzzle_key not in FAMILY_BY_KEY:
        raise HTTPException(status_code=400, detail=f"Unsupported co-op puzzle key '{puzzle_key}'.")
    stage_level = _compute_stage_level(session, room_key)
    run_nonce = str(session.get("run_nonce") or "legacy").strip() or "legacy"
    layout_seed = f"{session.get('seed', '')}|{room_key}|{puzzle_key}|{difficulty}"
    solution_seed = f"{layout_seed}|{run_nonce}"
    for attempt in range(MAX_PREFLIGHT_ATTEMPTS):
        candidate = _gen_state(
            puzzle_key,
            difficulty,
            layout_seed,
            f"{solution_seed}|attempt:{attempt}",
            stage_level,
        )
        trace = try_solve_v2(candidate)
        if trace is None:
            continue
        candidate["preflight_trace"] = trace
        candidate["preflight_validated"] = True
        return candidate
    fallback = _new_state(puzzle_key, difficulty, layout_seed, f"{solution_seed}|fallback", stage_level)
    fallback["runtime"] = {"owner_lock": False, "guest_lock": False}
    fallback["reset_runtime"] = deepcopy(fallback["runtime"])
    fallback["solution_script"] = [{"role": "owner", "cmd": "owner_lock"}, {"role": "guest", "cmd": "guest_lock"}]
    fallback["preflight_trace"] = deepcopy(fallback["solution_script"])
    fallback["preflight_validated"] = True
    return fallback


def ensure_current_room_puzzle_state_v2(session: dict[str, Any]) -> dict[str, Any]:
    room_key, room = _current_room_state(session)
    room_states = session.setdefault("room_puzzle_states", {})
    state = room_states.get(room_key)
    if not isinstance(state, dict) or int(state.get("schema_version", 0)) != 2:
        state = _create_state_with_preflight(session, room_key, room)
        room_states[room_key] = state
    tick_puzzle_state_v2(state, _utc_seconds())
    return state

def _serialize_stage_elements(state: dict[str, Any], now: float) -> list[dict[str, Any]]:
    runtime = state["runtime"]
    if state["key"] == "q":
        return [
            {
                "x": 180.0 + (idx * 180.0),
                "y": 360.0,
                "width": 130.0,
                "height": 130.0,
                "label": f"P{idx + 1}",
                "state": "target" if int(runtime["pressures"][idx]) == int(runtime["target"][idx]) else "active",
                "is_target": int(runtime["pressures"][idx]) == int(runtime["target"][idx]),
            }
            for idx in range(len(runtime["pressures"]))
        ]
    if state["key"] == "t":
        return [
            {
                "x": 170.0 + (idx * 170.0),
                "y": 300.0,
                "width": 120.0,
                "height": 210.0,
                "label": f"F{idx + 1}",
                "state": "target" if float(runtime["water"][idx]) <= int(runtime["safe"][idx]) else "active",
                "is_target": float(runtime["water"][idx]) <= int(runtime["safe"][idx]),
            }
            for idx in range(len(runtime["water"]))
        ]
    if state["key"] == "w":
        phase = _phase_value(now, float(runtime["speed"]), float(runtime["offset"]))
        near = _phase_distance(phase, float(runtime["target_phase"])) <= float(runtime["phase_tol"])
        return [{"x": 420.0, "y": 320.0, "width": 240.0, "height": 240.0, "label": "LOCK", "state": "target" if near else "active", "is_target": near}]
    return []


def _serialize_actions(state: dict[str, Any], role: str) -> list[dict[str, Any]]:
    runtime = state["runtime"]
    enabled = bool(state["can_interact"])
    key = state["key"]
    actions: list[dict[str, Any]] = []
    if key in {"p", "u", "v"}:
        actions.extend(
            [
                _mk_action("owner_down", "Owner -", "dial", "owner", enabled and role == "owner"),
                _mk_action("owner_up", "Owner +", "dial", "owner", enabled and role == "owner"),
                _mk_action("owner_lock" if key != "v" else "owner_arm", "Owner Lock" if key != "v" else "Owner Arm", "lock", "owner", enabled and role == "owner", bool(runtime["owner_lock"])),
                _mk_action("guest_down", "Guest -", "dial", "guest", enabled and role == "guest"),
                _mk_action("guest_up", "Guest +", "dial", "guest", enabled and role == "guest"),
                _mk_action("guest_lock" if key != "v" else "guest_arm", "Guest Lock" if key != "v" else "Guest Arm", "lock", "guest", enabled and role == "guest", bool(runtime["guest_lock"])),
            ]
        )
        if key == "v":
            actions.append(_mk_action("launch", "Launch", "launch", "shared", enabled and role == "guest"))
    elif key == "q":
        for idx in range(len(runtime["pressures"])):
            actions.append(_mk_action(f"owner_raise_{idx}", f"Raise P{idx + 1}", "valve", "owner", enabled and role == "owner"))
            actions.append(_mk_action(f"guest_vent_{idx}", f"Vent P{idx + 1}", "vent", "guest", enabled and role == "guest"))
        actions.append(_mk_action("owner_lock", "Owner Commit", "commit", "owner", enabled and role == "owner", bool(runtime["owner_lock"])))
        actions.append(_mk_action("guest_lock", "Guest Commit", "commit", "guest", enabled and role == "guest", bool(runtime["guest_lock"])))
    elif key == "r":
        for idx in range(len(runtime["active"])):
            tone = "owner" if idx in set(runtime["owner"]) else "guest"
            actions.append(_mk_action(f"toggle_{idx}", f"Beam {idx + 1}", "bridge", tone, enabled and ((tone == "owner" and role == "owner") or (tone == "guest" and role == "guest")), bool(runtime["active"][idx])))
    elif key == "s":
        for idx in range(len(runtime["owner_values"])):
            actions.append(_mk_action(f"owner_cycle_{idx}", f"O {idx + 1}", "mirror", "owner", enabled and role == "owner"))
            actions.append(_mk_action(f"guest_cycle_{idx}", f"G {idx + 1}", "mirror", "guest", enabled and role == "guest"))
    elif key == "t":
        for idx in range(len(runtime["water"])):
            actions.append(_mk_action(f"gate_{idx}", f"Gate {idx + 1}", "gate", "owner", enabled and role == "owner", bool(runtime["gates"][idx])))
            actions.append(_mk_action(f"pump_{idx}", f"Pump {idx + 1}", "pump", "guest", enabled and role == "guest"))
        actions.append(_mk_action("owner_lock", "Owner Lock", "commit", "owner", enabled and role == "owner", bool(runtime["owner_lock"])))
        actions.append(_mk_action("guest_lock", "Guest Lock", "commit", "guest", enabled and role == "guest", bool(runtime["guest_lock"])))
    elif key == "w":
        actions.append(_mk_action("catch", "Catch Window", "time", "shared", enabled))
    elif key == "x":
        for idx in range(len(runtime["layers"])):
            tone = "owner" if idx in set(runtime["owner"]) else "guest"
            usable = enabled and ((tone == "owner" and role == "owner") or (tone == "guest" and role == "guest"))
            actions.append(_mk_action(f"shift_{idx}_down", f"L{idx + 1} -", "strata", tone, usable))
            actions.append(_mk_action(f"shift_{idx}_up", f"L{idx + 1} +", "strata", tone, usable))
        actions.append(_mk_action("owner_lock", "Owner Lock", "commit", "owner", enabled and role == "owner", bool(runtime.get("owner_lock"))))
        actions.append(_mk_action("guest_lock", "Guest Lock", "commit", "guest", enabled and role == "guest", bool(runtime.get("guest_lock"))))
    elif key == "y":
        actions.extend(
            [
                _mk_action("guest_freq_down", "Freq -", "freq", "guest", enabled and role == "guest"),
                _mk_action("guest_freq_up", "Freq +", "freq", "guest", enabled and role == "guest"),
                _mk_action("fire", "Fire Pulse", "echo", "owner", enabled and role == "owner"),
            ]
        )
    elif key == "z":
        for r in range(int(runtime["rows"])):
            for c in range(int(runtime["cols"])):
                actions.append(_mk_action(f"past_{r}_{c}", f"P {r + 1},{c + 1}", "time", "owner", enabled and role == "owner", bool(runtime["past"][r][c])))
                actions.append(_mk_action(f"present_{r}_{c}", f"N {r + 1},{c + 1}", "time", "guest", enabled and role == "guest", bool(runtime["present"][r][c])))
    else:
        actions.append(_mk_action("owner_lock", "Owner Ready", "commit", "owner", enabled and role == "owner"))
        actions.append(_mk_action("guest_lock", "Guest Ready", "commit", "guest", enabled and role == "guest"))
    return actions


def _serialize_hud(state: dict[str, Any], now: float) -> list[dict[str, Any]]:
    runtime = state["runtime"]
    key = state["key"]
    if key in {"p", "u", "v"}:
        return [_mk_hud("Owner", f"{runtime['a']}->{runtime['target_a']}", "dial", "owner"), _mk_hud("Guest", f"{runtime['b']}->{runtime['target_b']}", "dial", "guest")]
    if key == "q":
        return [_mk_hud("Pressure", " | ".join(f"{runtime['pressures'][idx]}/{runtime['target'][idx]}" for idx in range(len(runtime["target"]))), "valve")]
    if key == "r":
        matched = sum(1 for i in range(len(runtime["required"])) if bool(runtime["active"][i]) == bool(runtime["required"][i]))
        return [_mk_hud("Match", f"{matched}/{len(runtime['required'])}", "bridge")]
    if key == "s":
        owner_match = sum(1 for i in range(len(runtime["owner_target"])) if int(runtime["owner_values"][i]) == int(runtime["owner_target"][i]))
        guest_match = sum(1 for i in range(len(runtime["guest_target"])) if int(runtime["guest_values"][i]) == int(runtime["guest_target"][i]))
        return [_mk_hud("Owner", f"{owner_match}/{len(runtime['owner_target'])}", "mirror", "owner"), _mk_hud("Guest", f"{guest_match}/{len(runtime['guest_target'])}", "mirror", "guest")]
    if key == "t":
        return [_mk_hud("Water", " | ".join(f"{int(runtime['water'][idx])}/{runtime['safe'][idx]}" for idx in range(len(runtime["safe"]))), "flood"), _mk_hud("Hold", f"{runtime['hold']:.1f}/1.0s", "time")]
    if key == "w":
        phase = _phase_value(now, float(runtime["speed"]), float(runtime["offset"]))
        return [_mk_hud("Phase", f"{phase:.2f}->{runtime['target_phase']:.2f}", "time"), _mk_hud("Catch", f"{runtime['catches']}/{runtime['required']}", "lock")]
    if key == "x":
        matched = sum(1 for i in range(len(runtime["target"])) if int(runtime["layers"][i]) == int(runtime["target"][i]))
        return [_mk_hud("Aligned", f"{matched}/{len(runtime['target'])}", "strata")]
    if key == "y":
        phase = _phase_value(now, float(runtime["speed"]), float(runtime["offset"]))
        return [_mk_hud("Freq", f"{runtime['freq']}->{runtime['target_freq']}", "freq"), _mk_hud("Phase", f"{phase:.2f}->{runtime['target_phase']:.2f}", "time"), _mk_hud("Res", f"{runtime['res']}/{runtime['required']}", "echo")]
    if key == "z":
        past_now = sum(1 for row in runtime["past"] for v in row if v)
        present_now = sum(1 for row in runtime["present"] for v in row if v)
        return [_mk_hud("Past", f"{past_now}/{sum(runtime['past_clues'])}", "time", "owner"), _mk_hud("Now", f"{present_now}/{sum(runtime['present_clues'])}", "time", "guest")]
    return [_mk_hud("Fallback", "Dual commit", "commit")]

def serialize_current_room_puzzle_v2(session: dict[str, Any], username: str) -> dict[str, Any]:
    state = ensure_current_room_puzzle_state_v2(session)
    role = _role(username, session)
    now = _utc_seconds()
    tick_puzzle_state_v2(state, now)
    hud = _serialize_hud(state, now)
    actions = _serialize_actions(state, role)
    stage_elements = _serialize_stage_elements(state, now)
    prompt = "Press E to open the panel. Use your assigned controls."
    view = {
        "schema_version": 2,
        "family_id": state["family_id"],
        "phase": state["phase"],
        "status_code": state["status_code"],
        "status_text": state["status_text"],
        "is_solved": bool(state["is_solved"]),
        "can_interact": bool(state["can_interact"]),
        "progress": float(state["progress"]),
        "progress_label": state.get("progress_label", "System Stability"),
        "progress_value": float(state.get("progress_value", state.get("progress", 0.0))),
        "progress_trend": state.get("progress_trend", "steady"),
        "attempt": int(state["attempt"]),
        "failure_code": state.get("failure_code", ""),
        "failure_label": state.get("failure_label", ""),
        "failure_visual_cue": state.get("failure_visual_cue", ""),
        "recovery_text": state.get("recovery_text", ""),
        "stage_level": int(state.get("stage_level", 1)),
        "stage_visual_profile": state.get("stage_visual_profile", "intro"),
        "accent_color": state["accent_color"],
        "mechanic_type": state["mechanic_type"],
        "hud": hud,
        "actions": actions,
        "stage_elements": stage_elements,
        "prompt": prompt,
        "panel": {
            "status_text": state["status_text"],
            "prompt": prompt,
            "hud": hud,
            "actions": actions,
            "failure_code": state.get("failure_code", ""),
            "failure_label": state.get("failure_label", ""),
            "failure_visual_cue": state.get("failure_visual_cue", ""),
            "recovery_text": state.get("recovery_text", ""),
            "progress_label": state.get("progress_label", "System Stability"),
            "progress_value": float(state.get("progress_value", state.get("progress", 0.0))),
            "progress_trend": state.get("progress_trend", "steady"),
            "stage_level": int(state.get("stage_level", 1)),
            "stage_visual_profile": state.get("stage_visual_profile", "intro"),
        },
        "stage": {
            "visual_profile": state.get("stage_visual_profile", "intro"),
            "level": int(state.get("stage_level", 1)),
            "elements": stage_elements,
        },
    }
    return {
        "key": state["key"],
        "difficulty": session.get("difficulty"),
        "name": state["name"],
        "instruction": state["instruction"],
        "status": state["status_text"],
        "completed": bool(state["is_solved"]),
        "role": role,
        "view_type": "coop_v2",
        "view": view,
    }


def update_position_puzzle_state_v2(session: dict[str, Any]) -> None:
    state = ensure_current_room_puzzle_state_v2(session)
    tick_puzzle_state_v2(state, _utc_seconds())


def apply_puzzle_action_v2(session: dict[str, Any], username: str, action: str, args: dict[str, Any] | None = None) -> dict[str, Any]:
    state = ensure_current_room_puzzle_state_v2(session)
    role = _role(username, session)
    now = _utc_seconds()
    tick_puzzle_state_v2(state, now)
    if state["is_solved"]:
        return state
    normalized = str(action or "").strip().lower()
    payload = args or {}
    cmd = str(payload.get("cmd") or "").strip().lower() if normalized == "v2_action" else normalized
    if not cmd:
        raise HTTPException(status_code=400, detail="Missing co-op puzzle command.")
    if not _command_allowed(state, role, cmd):
        raise HTTPException(status_code=403, detail="That control belongs to your partner.")
    if not bool(state["can_interact"]) and cmd != "wait":
        raise HTTPException(status_code=409, detail="Puzzle controls are cooling down.")
    _apply_cmd(state, role, cmd, now)
    tick_puzzle_state_v2(state, now, 0.0)
    return state


# In-place upgrade compatibility: keep original runtime entry points and v1 aliases.
def ensure_current_room_puzzle_state(session: dict[str, Any]) -> dict[str, Any]:
    return ensure_current_room_puzzle_state_v2(session)


def serialize_current_room_puzzle(session: dict[str, Any], username: str) -> dict[str, Any]:
    return serialize_current_room_puzzle_v2(session, username)


def update_position_puzzle_state(session: dict[str, Any]) -> None:
    update_position_puzzle_state_v2(session)


def apply_puzzle_action(session: dict[str, Any], username: str, action: str, args: dict[str, Any] | None = None) -> dict[str, Any]:
    return apply_puzzle_action_v2(session, username, action, args)
