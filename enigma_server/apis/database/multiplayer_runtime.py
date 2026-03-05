from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

from fastapi import HTTPException

from .multiplayer_puzzles import CO_OP_PUZZLE_CATALOG

ROOM_SIZE = 1080.0


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _utc_now_iso() -> str:
    return _utc_now().isoformat()


def _parse_iso(value: str | None) -> datetime:
    if not value:
        return _utc_now()

    try:
        return datetime.fromisoformat(value)
    except ValueError:
        return _utc_now()


def _stable_hash(value: str) -> int:
    hash_value = 2166136261
    for character in value:
        hash_value ^= ord(character)
        hash_value = (hash_value * 16777619) & 0xFFFFFFFF
    return hash_value & 0x7FFFFFFF


def _roll(seed: str, minimum: int, maximum: int) -> int:
    span = max(1, maximum - minimum + 1)
    return minimum + (_stable_hash(seed) % span)


def _room_key(session: dict[str, Any]) -> str:
    room = session.get("current_room", {})
    return f"{room.get('x')},{room.get('y')}"


def _current_room_state(session: dict[str, Any]) -> tuple[str, dict[str, Any]]:
    room_key = _room_key(session)
    room = session.get("room_lookup", {}).get(room_key)
    if not room:
        raise HTTPException(status_code=409, detail="Current multiplayer room is missing.")
    return room_key, room


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


def _get_players(session: dict[str, Any]) -> tuple[dict[str, Any], dict[str, Any]]:
    owner_username = str(session.get("owner_username") or "").strip()
    guest_username = str(session.get("guest_username") or "").strip()
    players = session.get("players", {})
    owner = players.get(owner_username)
    guest = players.get(guest_username)
    if not owner or not guest:
        raise HTTPException(status_code=409, detail="Both co-op players must be connected.")
    return owner, guest


def _player_center(player: dict[str, Any]) -> tuple[float, float]:
    position = player.get("position", {})
    width = float(position.get("width", 8.0) or 8.0)
    height = float(position.get("height", 8.0) or 8.0)
    x = float(position.get("x", 0.0) or 0.0) + (width / 2.0)
    y = float(position.get("y", 0.0) or 0.0) + (height / 2.0)
    return x, y


def _center_in_rect(player: dict[str, Any], rect: dict[str, Any]) -> bool:
    x, y = _player_center(player)
    left = float(rect.get("x", 0.0) or 0.0)
    top = float(rect.get("y", 0.0) or 0.0)
    width = float(rect.get("width", 0.0) or 0.0)
    height = float(rect.get("height", 0.0) or 0.0)
    return left <= x <= left + width and top <= y <= top + height


def _create_stage_rect(center_x: float, center_y: float, width: float = 150.0, height: float = 150.0) -> dict[str, float]:
    return {
        "x": round(center_x - (width / 2.0), 3),
        "y": round(center_y - (height / 2.0), 3),
        "width": round(width, 3),
        "height": round(height, 3),
    }


def _named_rects(names: list[str]) -> list[dict[str, Any]]:
    positions = {
        2: [(310.0, 540.0), (770.0, 540.0)],
        3: [(250.0, 540.0), (540.0, 320.0), (830.0, 540.0)],
        4: [(260.0, 360.0), (820.0, 360.0), (260.0, 760.0), (820.0, 760.0)],
    }
    centers = positions.get(len(names), positions[4])
    rects: list[dict[str, Any]] = []
    for index, name in enumerate(names):
        rects.append({"id": index, "label": name, **_create_stage_rect(*centers[index])})
    return rects


def _zone_rects(labels: list[str], columns: int = 3) -> list[dict[str, Any]]:
    cell_width = 210.0
    cell_height = 170.0
    start_x = 170.0
    start_y = 260.0
    gap_x = 120.0
    gap_y = 130.0
    rects: list[dict[str, Any]] = []
    for index, label in enumerate(labels):
        row = index // columns
        column = index % columns
        rects.append(
            {
                "id": index,
                "label": label,
                "x": round(start_x + (column * (cell_width + gap_x)), 3),
                "y": round(start_y + (row * (cell_height + gap_y)), 3),
                "width": cell_width,
                "height": cell_height,
            }
        )
    return rects


def _role(username: str, session: dict[str, Any]) -> str:
    return "owner" if username == str(session.get("owner_username") or "").strip() else "guest"


def _other_role(role: str) -> str:
    return "guest" if role == "owner" else "owner"


def _pulse_value(started_at: str, speed: float, offset: float, now: datetime | None = None) -> float:
    current = now or _utc_now()
    elapsed = max(0.0, (current - _parse_iso(started_at)).total_seconds())
    value = (offset + (elapsed * speed)) % 2.0
    return value if value <= 1.0 else 2.0 - value


def _direction_transform(direction: str, rule: str) -> str:
    order = ["Up", "Right", "Down", "Left"]
    index = order.index(direction)
    if rule == "mirror":
        return {"Up": "Up", "Down": "Down", "Left": "Right", "Right": "Left"}[direction]
    if rule == "invert":
        return {"Up": "Down", "Down": "Up", "Left": "Right", "Right": "Left"}[direction]
    if rule == "rotate":
        return order[(index + 1) % len(order)]
    if rule == "rotate180":
        return order[(index + 2) % len(order)]
    return direction


def _transform_pattern(pattern: list[str], rule: str) -> list[str]:
    if rule == "reverse":
        return list(reversed(pattern))
    return [_direction_transform(direction, rule) for direction in pattern]


def _complete(state: dict[str, Any], message: str) -> None:
    state["completed"] = True
    state["status"] = message


def _create_pressure_state(seed: str, difficulty: str) -> dict[str, Any]:
    if difficulty == "easy":
        return {
            "key": "p",
            "view_type": "pressure_systems",
            "name": CO_OP_PUZZLE_CATALOG[difficulty]["p"]["name"],
            "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["p"]["description"],
            "plates": _named_rects(["Owner Plate", "Guest Plate"]),
            "phases": [{"owner": 0, "guest": 1}],
            "phase_index": 0,
            "hold_seconds": 0.8,
            "hold_started_at": None,
            "completed": False,
            "status": "Stand on both linked plates together.",
        }

    if difficulty == "medium":
        names = ["Dawn", "Zenith", "Dusk"]
        owner_plate = _roll(f"{seed}|pressure|owner", 0, 2)
        guest_plate = (owner_plate + 2) % 3
        return {
            "key": "p",
            "view_type": "pressure_systems",
            "name": CO_OP_PUZZLE_CATALOG[difficulty]["p"]["name"],
            "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["p"]["description"],
            "plates": _named_rects(names),
            "phases": [{"owner": owner_plate, "guest": guest_plate}],
            "phase_index": 0,
            "hold_seconds": 1.0,
            "hold_started_at": None,
            "owner_clue": [
                "The owner stands where the sun rises.",
                "The owner takes the high noon plate.",
                "The owner anchors the falling light.",
            ][owner_plate],
            "guest_clue": [
                "The guest answers from the dawn plate.",
                "The guest steadies the zenith plate.",
                "The guest grounds the cold dusk plate.",
            ][guest_plate],
            "completed": False,
            "status": "Find the correct pair of plates and hold them together.",
        }

    first_owner = _roll(f"{seed}|pressure|hard|owner1", 0, 3)
    first_guest = (first_owner + 2) % 4
    return {
        "key": "p",
        "view_type": "pressure_systems",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["p"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["p"]["description"],
        "plates": _named_rects(["Ember", "Tide", "Volt", "Ash"]),
        "phases": [
            {"owner": first_owner, "guest": first_guest},
            {"owner": first_guest, "guest": first_owner},
            {"owner": (first_owner + 1) % 4, "guest": (first_guest + 1) % 4},
        ],
        "phase_index": 0,
        "hold_seconds": 1.15,
        "hold_started_at": None,
        "owner_clue": "Stabilize the outer pair, then swap, then crest one plate clockwise.",
        "guest_clue": "Mirror the owner's resonance, then swap, then rise one plate clockwise.",
        "completed": False,
        "status": "Charge each resonance phase in order without breaking formation.",
    }

def _create_reaction_state(seed: str, difficulty: str) -> dict[str, Any]:
    base = {
        "key": "q",
        "view_type": "sync_reaction",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["q"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["q"]["description"],
        "started_at": _utc_now_iso(),
        "window_start": (_roll(f"{seed}|reaction|window", 24, 58) / 100.0),
        "window_width": 0.16 if difficulty == "easy" else 0.13 if difficulty == "medium" else 0.14,
        "owner_speed": 0.65 if difficulty == "easy" else 0.86,
        "guest_speed": 0.65 if difficulty == "easy" else 1.04 if difficulty == "medium" else 0.92,
        "owner_offset": (_roll(f"{seed}|reaction|owner", 8, 42) / 100.0),
        "guest_offset": (_roll(f"{seed}|reaction|guest", 51, 88) / 100.0),
        "locks": {},
        "completed": False,
        "status": "Coordinate your stop timing with your partner.",
    }
    if difficulty == "hard":
        base["sync_tolerance_seconds"] = 0.35
        base["show_target_owner"] = True
        base["show_pulse_owner"] = False
        base["show_target_guest"] = False
        base["show_pulse_guest"] = True
        base["status"] = "Owner calls the lock. Guest catches the pulse."
        return base

    base["sync_tolerance_seconds"] = 0.5 if difficulty == "easy" else 0.4
    base["show_target_owner"] = True
    base["show_pulse_owner"] = True
    base["show_target_guest"] = True
    base["show_pulse_guest"] = True
    return base


def _riddle_pool() -> dict[str, list[dict[str, Any]]]:
    return {
        "easy": [
            {
                "owner_prompt": "I speak without a mouth and answer every shout. What am I?",
                "guest_prompt": "Choose the answer your partner describes.",
                "owner_options": ["A", "B", "C", "D"],
                "guest_options": ["Echo", "River", "Lantern", "Compass"],
                "answer_index": 0,
            },
            {
                "owner_prompt": "What has keys but can never open a lock?",
                "guest_prompt": "Pick the right artifact.",
                "owner_options": ["A", "B", "C", "D"],
                "guest_options": ["Piano", "Anchor", "Bell", "Mirror"],
                "answer_index": 0,
            },
            {
                "owner_prompt": "The more you take, the more you leave behind. What are they?",
                "guest_prompt": "Listen for the clue and choose.",
                "owner_options": ["A", "B", "C", "D"],
                "guest_options": ["Footsteps", "Stars", "Shadows", "Leaves"],
                "answer_index": 0,
            },
        ],
        "medium": [
            {
                "owner_clues": ["The answer is metallic.", "It is not carried by sound."],
                "guest_clues": ["It points but never speaks.", "It is not the silver bell."],
                "options": ["Copper Compass", "Silver Bell", "Glass Orchid", "Velvet Drum"],
                "answer_index": 0,
            },
            {
                "owner_clues": ["The answer is alive.", "Its color is not crimson."],
                "guest_clues": ["It grows, not forges.", "It is not the midnight fern."],
                "options": ["Azure Orchid", "Crimson Blade", "Midnight Fern", "Amber Gear"],
                "answer_index": 0,
            },
            {
                "owner_clues": ["The answer reflects light.", "It is not forged from wood."],
                "guest_clues": ["It hangs on walls.", "It is not the bronze lantern."],
                "options": ["Silver Mirror", "Bronze Lantern", "Oak Spear", "Iron Wheel"],
                "answer_index": 0,
            },
        ],
        "hard": [
            {
                "owner_clues": ["Statue A: 'The left lever is false.'", "Statue B: 'Only one of us tells the truth.'"],
                "guest_clues": ["Statue C: 'The center lever is false.'", "The true path is guarded by exactly one honest statue."],
                "options": ["Left Lever", "Center Lever", "Right Lever"],
                "answer_index": 2,
            },
            {
                "owner_clues": ["Stone One: 'The right lever lies.'", "Stone Two: 'The center path is safe.'"],
                "guest_clues": ["Stone Three: 'Exactly two stones lie.'", "Only the consistent assignment opens the chamber."],
                "options": ["Left Lever", "Center Lever", "Right Lever"],
                "answer_index": 0,
            },
            {
                "owner_clues": ["Cipher A: 'The left lever is true.'", "Cipher B: 'Cipher A lies.'"],
                "guest_clues": ["Cipher C: 'The center lever is false.'", "Only one statement survives the paradox."],
                "options": ["Left Lever", "Center Lever", "Right Lever"],
                "answer_index": 1,
            },
        ],
    }


def _create_riddle_state(seed: str, difficulty: str) -> dict[str, Any]:
    pool = _riddle_pool()[difficulty]
    entry = pool[_stable_hash(f"{seed}|riddle|{difficulty}") % len(pool)]
    state = {
        "key": "r",
        "view_type": "deduction_riddle",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["r"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["r"]["description"],
        "answer_index": entry["answer_index"],
        "owner_selection": None,
        "guest_selection": None,
        "completed": False,
        "status": "Both players must commit to the same answer.",
    }
    state.update(entry)
    return state


def _create_memory_state(seed: str, difficulty: str) -> dict[str, Any]:
    symbols = ["A", "B", "C", "D", "E", "F"]
    length = 6 if difficulty == "easy" else 7 if difficulty == "medium" else 8
    sequence = [symbols[_stable_hash(f"{seed}|memory|{difficulty}|{index}") % len(symbols)] for index in range(length)]
    if difficulty == "easy":
        owner_view = sequence[: length // 2]
        guest_view = sequence[length // 2 :]
        roles = ["owner"] * (length // 2) + ["guest"] * (length - (length // 2))
    else:
        owner_view = sequence[:]
        guest_view = sequence[:]
        owner_view[_roll(f"{seed}|memory|owner|swap", 0, length - 1)] = symbols[_roll(f"{seed}|memory|owner|symbol", 0, len(symbols) - 1)]
        guest_view[_roll(f"{seed}|memory|guest|swap", 0, length - 1)] = symbols[_roll(f"{seed}|memory|guest|symbol", 0, len(symbols) - 1)]
        roles = ["owner" if index % 2 == 0 else "guest" for index in range(length)]
    return {
        "key": "s",
        "view_type": "split_memory",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["s"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["s"]["description"],
        "symbols": symbols,
        "sequence": sequence,
        "owner_view": owner_view,
        "guest_view": guest_view,
        "input": [],
        "input_roles": roles,
        "completed": False,
        "status": "Reconstruct the shared sequence together.",
    }


def _create_rotation_state(seed: str, difficulty: str) -> dict[str, Any]:
    rows = 2
    cols = 2 if difficulty == "easy" else 3
    total = rows * cols
    current = [(_stable_hash(f"{seed}|rotation|current|{index}") % 4) for index in range(total)]
    owner_controls = [index for index in range(total) if index % 2 == 0]
    guest_controls = [index for index in range(total) if index % 2 == 1]
    mirror_pairs = {index: (total - 1 - index) for index in range(total)}
    targets = list(current)
    step_count = 3 if difficulty == "easy" else 5 if difficulty == "medium" else 6
    for step_index in range(step_count):
        role = "owner" if _roll(f"{seed}|rotation|role|{step_index}", 0, 1) == 0 else "guest"
        controls = owner_controls if role == "owner" else guest_controls
        control_index = controls[_roll(f"{seed}|rotation|tile|{step_index}", 0, len(controls) - 1)]
        targets[control_index] = (int(targets[control_index]) + 1) % 4
        if difficulty == "medium":
            mirror_id = mirror_pairs.get(control_index)
            if mirror_id is not None and 0 <= int(mirror_id) < len(targets):
                targets[int(mirror_id)] = (int(targets[int(mirror_id)]) + 1) % 4
    if current == targets:
        fallback_index = owner_controls[0]
        targets[fallback_index] = (int(targets[fallback_index]) + 1) % 4
        if difficulty == "medium":
            mirror_id = mirror_pairs.get(fallback_index)
            if mirror_id is not None and 0 <= int(mirror_id) < len(targets):
                targets[int(mirror_id)] = (int(targets[int(mirror_id)]) + 1) % 4
    return {
        "key": "t",
        "view_type": "dual_rotation",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["t"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["t"]["description"],
        "rows": rows,
        "cols": cols,
        "current": current,
        "targets": targets,
        "owner_controls": owner_controls,
        "guest_controls": guest_controls,
        "mirror_pairs": mirror_pairs,
        "board_rotation_owner": 0,
        "board_rotation_guest": 1 if difficulty == "hard" else 0,
        "completed": False,
        "status": "Rotate your tiles until the shared path snaps into alignment.",
    }


def _create_pattern_state(seed: str, difficulty: str) -> dict[str, Any]:
    directions = ["Up", "Right", "Down", "Left"]
    length = 5 if difficulty == "easy" else 6 if difficulty == "medium" else 7
    pattern = [directions[_stable_hash(f"{seed}|pattern|{difficulty}|{index}") % len(directions)] for index in range(length)]
    rule = "reverse" if difficulty == "easy" else ["mirror", "invert", "rotate", "rotate180"][_stable_hash(f"{seed}|pattern|rule|{difficulty}") % 4]
    expected = pattern if difficulty == "easy" else _transform_pattern(pattern, rule)
    guest_expected = list(reversed(pattern)) if difficulty == "easy" else expected
    return {
        "key": "u",
        "view_type": "opposing_pattern_input",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["u"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["u"]["description"],
        "pattern": pattern,
        "rule": rule,
        "expected_owner": expected,
        "expected_guest": guest_expected,
        "owner_input": [],
        "guest_input": [],
        "completed": False,
        "status": "Decode the shared pattern rule and enter the correct sequence.",
    }

def _create_flow_state(seed: str, difficulty: str) -> dict[str, Any]:
    count = 3 if difficulty != "hard" else 4
    owner_values = [_roll(f"{seed}|flow|owner|value|{index}", 0, 3) for index in range(count)]
    guest_values = [_roll(f"{seed}|flow|guest|value|{index}", 0, 3) for index in range(count)]
    owner_controls = []
    guest_controls = []
    for index in range(count):
        owner_controls.append(
            {
                "label": f"Valve {index + 1}",
                "owner_delta": [1 if sub == index else 0 for sub in range(count)],
                "guest_delta": [1 if sub == (index + 1) % count else -1 if sub == (index + 2) % count else 0 for sub in range(count)],
            }
        )
        guest_controls.append(
            {
                "label": f"Valve {index + 1}",
                "owner_delta": [1 if sub == (index - 1) % count else -1 if sub == (index + 1) % count else 0 for sub in range(count)],
                "guest_delta": [1 if sub == index else 0 for sub in range(count)],
            }
        )
    owner_targets = list(owner_values)
    guest_targets = list(guest_values)
    sequence_length = 3 if difficulty == "easy" else 4 if difficulty == "medium" else 5
    for step_index in range(sequence_length):
        role = "owner" if _roll(f"{seed}|flow|role|{step_index}", 0, 1) == 0 else "guest"
        controls = owner_controls if role == "owner" else guest_controls
        control = controls[_roll(f"{seed}|flow|control|{step_index}", 0, len(controls) - 1)]
        owner_targets, guest_targets = _apply_flow_control_values(owner_targets, guest_targets, control)
    if owner_targets == owner_values and guest_targets == guest_values:
        owner_targets, guest_targets = _apply_flow_control_values(owner_targets, guest_targets, owner_controls[0])
    return {
        "key": "v",
        "view_type": "flow_transfer",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["v"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["v"]["description"],
        "owner_values": owner_values,
        "guest_values": guest_values,
        "owner_targets": owner_targets,
        "guest_targets": guest_targets,
        "owner_controls": owner_controls,
        "guest_controls": guest_controls,
        "completed": False,
        "status": "Redistribute flow without starving the shared network.",
    }


def _create_weight_state(seed: str, difficulty: str) -> dict[str, Any]:
    count = 3 if difficulty != "hard" else 4
    owner_multipliers = [_roll(f"{seed}|weight|owner|mult|{index}", 1, 3) for index in range(count)]
    guest_multipliers = [_roll(f"{seed}|weight|guest|mult|{index}", 1, 3) for index in range(count)]
    if difficulty == "hard":
        owner_multipliers[0] = 2
        guest_multipliers[-1] = -1
    return {
        "key": "w",
        "view_type": "distributed_weight",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["w"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["w"]["description"],
        "owner_allocations": [0] * count,
        "guest_allocations": [0] * count,
        "owner_multipliers": owner_multipliers,
        "guest_multipliers": guest_multipliers,
        "limit_per_pad": 4 if difficulty == "hard" else 3,
        "target": _roll(f"{seed}|weight|target|{difficulty}", 6, 18 if difficulty == "hard" else 12),
        "completed": False,
        "status": "Balance the shared equation exactly.",
    }


def _create_binary_state(seed: str, difficulty: str) -> dict[str, Any]:
    if difficulty == "easy":
        return {
            "key": "x",
            "view_type": "binary_echo",
            "name": CO_OP_PUZZLE_CATALOG[difficulty]["x"]["name"],
            "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["x"]["description"],
            "mode": "toggle_bits",
            "current": [False] * 6,
            "target": [bool(_stable_hash(f"{seed}|binary|easy|{index}") % 2) for index in range(6)],
            "owner_controls": [0, 2, 4],
            "guest_controls": [1, 3, 5],
            "completed": False,
            "status": "Match the shared binary target.",
        }

    current = [bool(_stable_hash(f"{seed}|binary|current|{difficulty}|{index}") % 2) for index in range(7 if difficulty == "hard" else 6)]
    target = [bool(_stable_hash(f"{seed}|binary|target|{difficulty}|{index}") % 2) for index in range(len(current))]
    if difficulty == "medium":
        return {
            "key": "x",
            "view_type": "binary_echo",
            "name": CO_OP_PUZZLE_CATALOG[difficulty]["x"]["name"],
            "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["x"]["description"],
            "mode": "shared_operations",
            "current": current,
            "target": target,
            "owner_operations": ["flip_left", "rotate_left"],
            "guest_operations": ["flip_right", "invert_even"],
            "completed": False,
            "status": "Use your half of the machine without scrambling your partner's side.",
        }

    return {
        "key": "x",
        "view_type": "binary_echo",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["x"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["x"]["description"],
        "mode": "distributed_ops",
        "current": current,
        "target": target,
        "owner_operations": ["rotate_left", "invert_all"],
        "guest_operations": ["flip_alternate", "xor_mask"],
        "xor_mask": [True, False, True, False, True, False, True][: len(current)],
        "moves_remaining": 8,
        "completed": False,
        "status": "Combine your separate operators to reach the target before the machine locks.",
    }


def _create_signal_state(seed: str, difficulty: str) -> dict[str, Any]:
    target = [0, 1, 2, 3]
    if difficulty == "medium":
        target = [0, 2, 1, 3]
    elif difficulty == "hard":
        target = [1, 0, 3, 2]
    blocked = [(1, 1), (2, 2)] if difficulty == "medium" else [(0, 2), (1, 3)] if difficulty == "hard" else []
    fake_blocked = [(0, 1), (2, 0)] if difficulty == "hard" else []
    return {
        "key": "y",
        "view_type": "signal_lines",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["y"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["y"]["description"],
        "left_nodes": [f"Signal {index + 1}" for index in range(4)],
        "right_nodes": ["I", "II", "III", "IV"],
        "target_map": target,
        "routes": [None] * 4,
        "owner_controls": [0, 1],
        "guest_controls": [2, 3],
        "blocked_routes": blocked,
        "fake_blocked_routes": fake_blocked,
        "completed": False,
        "status": "Route each signal to the correct receiver without breaking the grid.",
    }


def _create_spatial_state(seed: str, difficulty: str) -> dict[str, Any]:
    zones = _zone_rects(["A", "B", "C", "D", "E", "F"])
    if difficulty == "easy":
        sequence = [{"owner": 0, "guest": 1}]
        hold = 0.8
    elif difficulty == "medium":
        sequence = [{"owner": 0, "guest": 2}, {"owner": 1, "guest": 1}, {"owner": 2, "guest": 0}]
        hold = 0.9
    else:
        first = _roll(f"{seed}|spatial|hard|first", 0, 3)
        sequence = [
            {"owner": first, "guest": (first + 1) % 4},
            {"owner": (first + 2) % 4, "guest": first},
            {"owner": (first + 3) % 4, "guest": (first + 2) % 4},
            {"owner": first, "guest": (first + 3) % 4},
        ]
        hold = 1.0
    return {
        "key": "z",
        "view_type": "spatial_sync",
        "name": CO_OP_PUZZLE_CATALOG[difficulty]["z"]["name"],
        "instruction": CO_OP_PUZZLE_CATALOG[difficulty]["z"]["description"],
        "zones": zones,
        "sequence": sequence,
        "step_index": 0,
        "hold_seconds": hold,
        "hold_started_at": None,
        "completed": False,
        "status": "Stand in the active zone pair together.",
    }


def _create_puzzle_state(session: dict[str, Any], room_key: str, room: dict[str, Any]) -> dict[str, Any]:
    difficulty = str(session.get("difficulty") or "easy").strip().lower()
    puzzle_key = str(room.get("puzzle_key") or "").strip().lower()
    seed = f"{session.get('session_id')}|{room_key}|{puzzle_key}|{difficulty}"
    if puzzle_key == "p":
        return _create_pressure_state(seed, difficulty)
    if puzzle_key == "q":
        return _create_reaction_state(seed, difficulty)
    if puzzle_key == "r":
        return _create_riddle_state(seed, difficulty)
    if puzzle_key == "s":
        return _create_memory_state(seed, difficulty)
    if puzzle_key == "t":
        return _create_rotation_state(seed, difficulty)
    if puzzle_key == "u":
        return _create_pattern_state(seed, difficulty)
    if puzzle_key == "v":
        return _create_flow_state(seed, difficulty)
    if puzzle_key == "w":
        return _create_weight_state(seed, difficulty)
    if puzzle_key == "x":
        return _create_binary_state(seed, difficulty)
    if puzzle_key == "y":
        return _create_signal_state(seed, difficulty)
    if puzzle_key == "z":
        return _create_spatial_state(seed, difficulty)
    raise HTTPException(status_code=400, detail=f"Unsupported co-op puzzle key '{puzzle_key}'.")


def ensure_current_room_puzzle_state(session: dict[str, Any]) -> dict[str, Any]:
    room_key, room = _current_room_state(session)
    room_states = session.setdefault("room_puzzle_states", {})
    state = room_states.get(room_key)
    if not isinstance(state, dict):
        state = _create_puzzle_state(session, room_key, room)
        room_states[room_key] = state
    if _get_room_progress(session, room_key).get("puzzle_solved"):
        state["completed"] = True
    return state

def _serialize_pressure_view(state: dict[str, Any], role: str, owner: dict[str, Any], guest: dict[str, Any]) -> dict[str, Any]:
    phase_index = int(state.get("phase_index", 0) or 0)
    phases = state.get("phases", [])
    current_phase = phases[min(phase_index, len(phases) - 1)] if phases else {"owner": 0, "guest": 0}
    owner_plate = next((plate.get("id") for plate in state["plates"] if _center_in_rect(owner, plate)), None)
    guest_plate = next((plate.get("id") for plate in state["plates"] if _center_in_rect(guest, plate)), None)
    return {
        "stage_elements": [
            {
                **plate,
                "state": "active" if plate["id"] in {owner_plate, guest_plate} else "idle",
                "is_target": bool(not state.get("completed") and plate["id"] in {current_phase["owner"], current_phase["guest"]}),
            }
            for plate in state["plates"]
        ],
        "phase_index": phase_index,
        "phase_count": len(phases),
        "hold_seconds": state.get("hold_seconds", 0.0),
        "clue": state.get(f"{role}_clue") or state.get("instruction"),
    }


def _serialize_reaction_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    return {
        "show_target": bool(state.get(f"show_target_{role}", True)),
        "show_pulse": bool(state.get(f"show_pulse_{role}", True)),
        "target_start": state.get("window_start", 0.3),
        "target_width": state.get("window_width", 0.15),
        "pulse_started_at": state.get("started_at"),
        "pulse_speed": state.get(f"{role}_speed", 0.75),
        "pulse_offset": state.get(f"{role}_offset", 0.15),
        "locked_self": role in state.get("locks", {}),
        "locked_partner": _other_role(role) in state.get("locks", {}),
        "sync_tolerance_seconds": state.get("sync_tolerance_seconds", 0.5),
    }


def _serialize_riddle_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    if "owner_prompt" in state:
        return {
            "prompt": state["owner_prompt"] if role == "owner" else state["guest_prompt"],
            "options": state["owner_options"] if role == "owner" else state["guest_options"],
            "selected_self": state.get(f"{role}_selection"),
            "selected_partner": state.get(f"{_other_role(role)}_selection"),
        }
    return {
        "clues": state.get(f"{role}_clues", []),
        "options": state.get("options", []),
        "selected_self": state.get(f"{role}_selection"),
        "selected_partner": state.get(f"{_other_role(role)}_selection"),
    }


def _serialize_memory_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    input_values = state.get("input", [])
    input_roles = state.get("input_roles", [])
    next_role = input_roles[len(input_values)] if len(input_values) < len(input_roles) else None
    return {
        "symbols": state.get("symbols", []),
        "visible_sequence": state.get(f"{role}_view", []),
        "input": input_values,
        "next_role": next_role,
    }


def _serialize_rotation_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    controls = set(state.get(f"{role}_controls", []))
    board_rotation = int(state.get(f"board_rotation_{role}", 0) or 0)
    cols = int(state.get("cols", 2) or 2)
    tiles = []
    current = state.get("current", [])
    targets = state.get("targets", [])
    for tile_id, rotation in enumerate(current):
        tiles.append(
            {
                "id": tile_id,
                "row": tile_id // cols,
                "col": tile_id % cols,
                "rotation": int(rotation),
                "target": int(targets[tile_id]),
                "controllable": tile_id in controls,
                "display_rotation": (int(rotation) + board_rotation) % 4,
            }
        )
    return {
        "rows": state.get("rows", 2),
        "cols": state.get("cols", 2),
        "board_rotation": board_rotation,
        "tiles": tiles,
    }


def _serialize_pattern_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    if state.get("expected_owner") == state.get("pattern"):
        clue = state.get("pattern") if role == "owner" else list(reversed(state.get("pattern", [])))
    elif role == "owner":
        clue = state.get("rule")
    else:
        clue = state.get("pattern")
    return {
        "clue": clue,
        "input": state.get(f"{role}_input", []),
        "target_length": len(state.get("expected_owner", [])),
    }


def _serialize_flow_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    return {
        "values": state.get(f"{role}_values", []),
        "targets": state.get(f"{role}_targets", []),
        "controls": state.get(f"{role}_controls", []),
        "partner_values": state.get(f"{_other_role(role)}_values", []),
    }


def _weight_total(state: dict[str, Any]) -> int:
    total = sum(int(value) * int(multiplier) for value, multiplier in zip(state.get("owner_allocations", []), state.get("owner_multipliers", []), strict=False))
    total += sum(int(value) * int(multiplier) for value, multiplier in zip(state.get("guest_allocations", []), state.get("guest_multipliers", []), strict=False))
    if len(state.get("owner_allocations", [])) == 4 and len(state.get("guest_allocations", [])) == 4:
        total += sum(int(state["guest_allocations"][index]) for index in range(0, 4, 2))
        total -= sum(int(state["owner_allocations"][index]) for index in range(1, 4, 2))
    return total


def _serialize_weight_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    return {
        "allocations": state.get(f"{role}_allocations", []),
        "multipliers": state.get(f"{role}_multipliers", []),
        "limit_per_pad": state.get("limit_per_pad", 3),
        "target": state.get("target", 0),
        "current_total": _weight_total(state),
    }


def _serialize_binary_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    payload = {"mode": state.get("mode"), "current": state.get("current", []), "target": state.get("target", [])}
    if state.get("mode") == "toggle_bits":
        payload["controls"] = state.get(f"{role}_controls", [])
    else:
        payload["operations"] = state.get(f"{role}_operations", [])
        payload["moves_remaining"] = state.get("moves_remaining")
    return payload


def _serialize_signal_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    return {
        "left_nodes": state.get("left_nodes", []),
        "right_nodes": state.get("right_nodes", []),
        "routes": state.get("routes", []),
        "controls": state.get(f"{role}_controls", []),
        "blocked_routes": state.get("blocked_routes", []) if role == "owner" else (state.get("fake_blocked_routes") or state.get("blocked_routes", [])),
        "real_routes_visible": role == "owner",
    }


def _serialize_spatial_view(state: dict[str, Any], role: str) -> dict[str, Any]:
    sequence = state.get("sequence", [])
    step_index = min(int(state.get("step_index", 0) or 0), max(0, len(sequence) - 1))
    current_step = sequence[step_index] if sequence else {"owner": 0, "guest": 0}
    return {
        "stage_elements": [
            {**zone, "state": "target" if zone["id"] == current_step.get(role) else "complete" if state.get("completed") else "idle"}
            for zone in state.get("zones", [])
        ],
        "step_index": step_index,
        "step_count": len(sequence),
        "hold_seconds": state.get("hold_seconds", 0.8),
        "target_zone": current_step.get(role),
    }


def serialize_current_room_puzzle(session: dict[str, Any], username: str) -> dict[str, Any]:
    state = ensure_current_room_puzzle_state(session)
    role = _role(username, session)
    owner, guest = _get_players(session)
    view_type = state.get("view_type")
    if view_type == "pressure_systems":
        view = _serialize_pressure_view(state, role, owner, guest)
    elif view_type == "sync_reaction":
        view = _serialize_reaction_view(state, role)
    elif view_type == "deduction_riddle":
        view = _serialize_riddle_view(state, role)
    elif view_type == "split_memory":
        view = _serialize_memory_view(state, role)
    elif view_type == "dual_rotation":
        view = _serialize_rotation_view(state, role)
    elif view_type == "opposing_pattern_input":
        view = _serialize_pattern_view(state, role)
    elif view_type == "flow_transfer":
        view = _serialize_flow_view(state, role)
    elif view_type == "distributed_weight":
        view = _serialize_weight_view(state, role)
    elif view_type == "binary_echo":
        view = _serialize_binary_view(state, role)
    elif view_type == "signal_lines":
        view = _serialize_signal_view(state, role)
    elif view_type == "spatial_sync":
        view = _serialize_spatial_view(state, role)
    else:
        view = {}
    return {
        "key": state.get("key"),
        "difficulty": session.get("difficulty"),
        "name": state.get("name"),
        "instruction": state.get("instruction"),
        "status": state.get("status"),
        "completed": bool(state.get("completed")),
        "role": role,
        "view_type": view_type,
        "view": view,
    }


def _update_pressure_state(session: dict[str, Any], state: dict[str, Any]) -> None:
    owner, guest = _get_players(session)
    phase_index = int(state.get("phase_index", 0) or 0)
    phases = state.get("phases", [])
    if phase_index >= len(phases):
        _complete(state, "The linked pressure lattice stabilizes.")
        return
    current = phases[phase_index]
    if _center_in_rect(owner, state["plates"][current["owner"]]) and _center_in_rect(guest, state["plates"][current["guest"]]):
        if not state.get("hold_started_at"):
            state["hold_started_at"] = _utc_now_iso()
            state["status"] = f"Charging resonance phase {phase_index + 1}/{len(phases)}..."
            return
        elapsed = (_utc_now() - _parse_iso(state.get("hold_started_at"))).total_seconds()
        if elapsed >= float(state.get("hold_seconds", 0.8)):
            state["phase_index"] = phase_index + 1
            state["hold_started_at"] = None
            if int(state["phase_index"]) >= len(phases):
                _complete(state, "The linked pressure lattice stabilizes.")
            else:
                state["status"] = f"Phase {phase_index + 1} stabilized. Shift to the next resonance pair."
        return
    state["hold_started_at"] = None
    state["status"] = "Both players must hold the active plate pair together."


def _update_spatial_state(session: dict[str, Any], state: dict[str, Any]) -> None:
    owner, guest = _get_players(session)
    step_index = int(state.get("step_index", 0) or 0)
    sequence = state.get("sequence", [])
    if step_index >= len(sequence):
        _complete(state, "Your movement patterns resonate perfectly.")
        return
    current = sequence[step_index]
    if _center_in_rect(owner, state["zones"][current["owner"]]) and _center_in_rect(guest, state["zones"][current["guest"]]):
        if not state.get("hold_started_at"):
            state["hold_started_at"] = _utc_now_iso()
            state["status"] = f"Locking sync zone {step_index + 1}/{len(sequence)}..."
            return
        elapsed = (_utc_now() - _parse_iso(state.get("hold_started_at"))).total_seconds()
        if elapsed >= float(state.get("hold_seconds", 0.8)):
            state["step_index"] = step_index + 1
            state["hold_started_at"] = None
            if int(state["step_index"]) >= len(sequence):
                _complete(state, "Your movement patterns resonate perfectly.")
            else:
                state["status"] = f"Sync step {step_index + 1} held. Advance together."
        return
    state["hold_started_at"] = None
    state["status"] = "Both players must stand in the current highlighted zones together."


def update_position_puzzle_state(session: dict[str, Any]) -> None:
    state = ensure_current_room_puzzle_state(session)
    if state.get("completed"):
        return
    if state.get("view_type") == "pressure_systems":
        _update_pressure_state(session, state)
    elif state.get("view_type") == "spatial_sync":
        _update_spatial_state(session, state)

def _apply_riddle_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    options = state.get("guest_options") or state.get("options") or []
    index = int(args.get("index", -1))
    if index < 0 or index >= len(options):
        raise HTTPException(status_code=400, detail="Invalid answer choice.")
    state[f"{role}_selection"] = index
    partner = state.get(f"{_other_role(role)}_selection")
    if partner is None:
        state["status"] = "Answer committed. Waiting for your partner."
        return
    if partner == index == int(state.get("answer_index", -1)):
        _complete(state, "Both minds converged on the true answer.")
        return
    if partner == index:
        state["owner_selection"] = None
        state["guest_selection"] = None
        state["status"] = "Both players committed to the wrong answer. The chamber resets."
        return
    state["status"] = "Your answers disagree. Align on one choice."


def _apply_memory_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    symbol = str(args.get("symbol") or "").strip().upper()
    valid_symbols = {str(entry).upper() for entry in state.get("symbols", [])}
    if symbol not in valid_symbols:
        raise HTTPException(status_code=400, detail="Invalid memory symbol.")
    current_input = list(state.get("input", []))
    input_roles = state.get("input_roles", [])
    next_index = len(current_input)
    if next_index >= len(input_roles):
        raise HTTPException(status_code=409, detail="This sequence is already complete.")
    if input_roles[next_index] != role:
        raise HTTPException(status_code=409, detail="It is your partner's turn to enter the next symbol.")
    if symbol != str(state.get("sequence", [])[next_index]).upper():
        state["input"] = []
        state["status"] = "The sequence fractured. Rebuild it from the start."
        return
    current_input.append(symbol)
    state["input"] = current_input
    if len(current_input) >= len(state.get("sequence", [])):
        _complete(state, "The split sequence recombines into one memory thread.")
    else:
        state["status"] = f"Sequence locked {len(current_input)}/{len(state.get('sequence', []))}."


def _apply_rotation_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    tile_id = int(args.get("tileId", -1))
    current = state.get("current", [])
    controls = set(state.get(f"{role}_controls", []))
    if tile_id not in controls or tile_id < 0 or tile_id >= len(current):
        raise HTTPException(status_code=403, detail="That tile is controlled by your partner.")
    current[tile_id] = (int(current[tile_id]) + 1) % 4
    if "mirrored" in str(state.get("instruction", "")).lower():
        mirror_id = state.get("mirror_pairs", {}).get(tile_id)
        if mirror_id is not None and 0 <= int(mirror_id) < len(current):
            current[int(mirror_id)] = (int(current[int(mirror_id)]) + 1) % 4
    state["current"] = current
    if current == state.get("targets"):
        _complete(state, "Both board halves align into a continuous path.")
    else:
        state["status"] = "Keep rotating until the shared path aligns."


def _apply_pattern_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    direction = str(args.get("direction") or "").strip().title()
    if direction not in {"Up", "Right", "Down", "Left"}:
        raise HTTPException(status_code=400, detail="Invalid direction.")
    key = f"{role}_input"
    target = list(state.get(f"expected_{role}", []))
    current = list(state.get(key, []))
    if len(current) >= len(target):
        raise HTTPException(status_code=409, detail="Your pattern is already complete.")
    if direction != target[len(current)]:
        state["owner_input"] = []
        state["guest_input"] = []
        state["status"] = "The transformed pattern shattered. Re-enter it from the start."
        return
    current.append(direction)
    state[key] = current
    if state.get("owner_input") == state.get("expected_owner") and state.get("guest_input") == state.get("expected_guest"):
        _complete(state, "Both transformed inputs resonate.")
    else:
        state["status"] = "One half of the transformed pattern is locked in."


def _clamp_values(values: list[int], minimum: int = 0, maximum: int = 9) -> list[int]:
    return [max(minimum, min(maximum, int(value))) for value in values]


def _apply_flow_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    controls = state.get(f"{role}_controls", [])
    index = int(args.get("index", -1))
    if index < 0 or index >= len(controls):
        raise HTTPException(status_code=400, detail="Invalid valve index.")
    control = controls[index]
    owner_values, guest_values = _apply_flow_control_values(state.get("owner_values", []), state.get("guest_values", []), control)
    state["owner_values"] = owner_values
    state["guest_values"] = guest_values
    if state.get("owner_values") == state.get("owner_targets") and state.get("guest_values") == state.get("guest_targets"):
        _complete(state, "The transfer network balances across both rooms.")
    else:
        state["status"] = "Flow shifted. Keep balancing the shared network."


def _apply_flow_control_values(owner_values: list[int], guest_values: list[int], control: dict[str, Any]) -> tuple[list[int], list[int]]:
    next_owner = _clamp_values([
        value + int(delta)
        for value, delta in zip(owner_values, control.get("owner_delta", []), strict=False)
    ])
    next_guest = _clamp_values([
        value + int(delta)
        for value, delta in zip(guest_values, control.get("guest_delta", []), strict=False)
    ])
    return next_owner, next_guest


def _apply_weight_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    allocations = list(state.get(f"{role}_allocations", []))
    index = int(args.get("index", -1))
    delta = int(args.get("delta", 0))
    if index < 0 or index >= len(allocations) or delta not in {-1, 1}:
        raise HTTPException(status_code=400, detail="Invalid weight adjustment.")
    limit = int(state.get("limit_per_pad", 3) or 3)
    allocations[index] = max(0, min(limit, int(allocations[index]) + delta))
    state[f"{role}_allocations"] = allocations
    total = _weight_total(state)
    if total == int(state.get("target", 0) or 0):
        _complete(state, "The distributed equation balances perfectly.")
    else:
        state["status"] = f"Current combined total: {total}. Keep adjusting toward the target."


def _flip(values: list[bool], indices: list[int]) -> list[bool]:
    clone = list(values)
    for index in indices:
        if 0 <= index < len(clone):
            clone[index] = not clone[index]
    return clone


def _apply_binary_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    current = list(state.get("current", []))
    mode = state.get("mode")
    if mode == "toggle_bits":
        index = int(args.get("index", -1))
        controls = set(state.get(f"{role}_controls", []))
        if index not in controls or index < 0 or index >= len(current):
            raise HTTPException(status_code=403, detail="That bit is controlled by your partner.")
        current[index] = not current[index]
    else:
        operation = str(args.get("operation") or "").strip()
        operations = set(state.get(f"{role}_operations", []))
        if operation not in operations:
            raise HTTPException(status_code=403, detail="That operator belongs to your partner.")
        if mode == "shared_operations":
            if operation == "flip_left":
                current = _flip(current, list(range(0, len(current) // 2)))
            elif operation == "rotate_left":
                current = current[1:] + current[:1]
            elif operation == "flip_right":
                current = _flip(current, list(range(len(current) // 2, len(current))))
            elif operation == "invert_even":
                current = _flip(current, list(range(0, len(current), 2)))
        else:
            moves_remaining = int(state.get("moves_remaining", 0) or 0)
            if moves_remaining <= 0:
                raise HTTPException(status_code=409, detail="The machine is out of moves.")
            state["moves_remaining"] = moves_remaining - 1
            if operation == "rotate_left":
                current = current[1:] + current[:1]
            elif operation == "invert_all":
                current = [not bit for bit in current]
            elif operation == "flip_alternate":
                current = _flip(current, list(range(0, len(current), 2)))
            elif operation == "xor_mask":
                mask = list(state.get("xor_mask", []))
                current = [bit ^ mask[index] for index, bit in enumerate(current)]
    state["current"] = current
    if current == state.get("target"):
        _complete(state, "The binary machine settles into the target state.")
    else:
        state["status"] = "The register shifted. Continue coordinating your operations."


def _apply_signal_action(state: dict[str, Any], role: str, args: dict[str, Any]) -> None:
    left_index = int(args.get("leftIndex", -1))
    right_index = int(args.get("rightIndex", -1))
    controls = set(state.get(f"{role}_controls", []))
    if left_index not in controls or left_index < 0 or left_index >= len(state.get("routes", [])) or right_index < 0 or right_index >= len(state.get("right_nodes", [])):
        raise HTTPException(status_code=400, detail="Invalid signal route.")
    if (left_index, right_index) in {tuple(pair) for pair in state.get("blocked_routes", [])}:
        raise HTTPException(status_code=409, detail="That route is blocked by the shared grid.")
    routes = list(state.get("routes", []))
    routes[left_index] = right_index
    state["routes"] = routes
    if all(route is not None for route in routes) and routes == state.get("target_map"):
        _complete(state, "The signal lattice routes cleanly across both halves.")
    else:
        state["status"] = "Route committed. Complete the remaining signal paths."


def _apply_reaction_action(state: dict[str, Any], role: str) -> None:
    locks = state.setdefault("locks", {})
    now = _utc_now()
    if role in locks:
        raise HTTPException(status_code=409, detail="You already locked your reaction.")
    if bool(state.get("show_target_guest", True)):
        meter = _pulse_value(state.get("started_at"), float(state.get(f"{role}_speed", 0.8) or 0.8), float(state.get(f"{role}_offset", 0.1) or 0.1), now)
        locks[role] = {"time": now.isoformat(), "meter": meter}
    else:
        guest_meter = _pulse_value(state.get("started_at"), float(state.get("guest_speed", 0.9) or 0.9), float(state.get("guest_offset", 0.15) or 0.15), now)
        locks[role] = {"time": now.isoformat(), "meter": guest_meter if role == "guest" else None}
    if len(locks) < 2:
        state["status"] = "Reaction locked. Waiting for your partner."
        return
    target_start = float(state.get("window_start", 0.3) or 0.3)
    target_end = target_start + float(state.get("window_width", 0.15) or 0.15)
    owner_lock = locks.get("owner", {})
    guest_lock = locks.get("guest", {})
    delta = abs((_parse_iso(owner_lock.get("time")) - _parse_iso(guest_lock.get("time"))).total_seconds())
    tolerance = float(state.get("sync_tolerance_seconds", 0.5) or 0.5)
    def in_window(value: float | None) -> bool:
        return value is not None and target_start <= float(value) <= target_end
    if bool(state.get("show_target_guest", True)):
        success = delta <= tolerance and in_window(owner_lock.get("meter")) and in_window(guest_lock.get("meter"))
    else:
        success = delta <= tolerance and in_window(guest_lock.get("meter"))
    if success:
        _complete(state, "Both reactions lock on the same beat.")
    else:
        state["locks"] = {}
        state["status"] = "The lock slipped. Both reaction channels restart."


def apply_puzzle_action(session: dict[str, Any], username: str, action: str, args: dict[str, Any] | None = None) -> dict[str, Any]:
    role = _role(username, session)
    state = ensure_current_room_puzzle_state(session)
    if state.get("completed"):
        return state
    payload = args or {}
    normalized_action = (action or "").strip().lower()
    if state.get("view_type") == "sync_reaction" and normalized_action == "lock":
        _apply_reaction_action(state, role)
    elif state.get("view_type") == "deduction_riddle" and normalized_action == "select_option":
        _apply_riddle_action(state, role, payload)
    elif state.get("view_type") == "split_memory" and normalized_action == "press_symbol":
        _apply_memory_action(state, role, payload)
    elif state.get("view_type") == "dual_rotation" and normalized_action == "rotate_tile":
        _apply_rotation_action(state, role, payload)
    elif state.get("view_type") == "opposing_pattern_input" and normalized_action == "press_direction":
        _apply_pattern_action(state, role, payload)
    elif state.get("view_type") == "flow_transfer" and normalized_action == "pulse_flow":
        _apply_flow_action(state, role, payload)
    elif state.get("view_type") == "distributed_weight" and normalized_action == "adjust_weight":
        _apply_weight_action(state, role, payload)
    elif state.get("view_type") == "binary_echo":
        _apply_binary_action(state, role, payload)
    elif state.get("view_type") == "signal_lines" and normalized_action == "route_signal":
        _apply_signal_action(state, role, payload)
    else:
        raise HTTPException(status_code=400, detail="Unsupported co-op puzzle action.")
    return state
