from __future__ import annotations

import copy
import sys
import unittest
from pathlib import Path

from fastapi import HTTPException

sys.path.append(str(Path(__file__).resolve().parents[1]))

from apis.database.multiplayer_runtime import (  # noqa: E402
    FAILURE_LANGUAGES,
    apply_puzzle_action_v2,
    ensure_current_room_puzzle_state_v2,
    serialize_current_room_puzzle_v2,
    tick_puzzle_state_v2,
    try_solve_v2,
)


PUZZLE_KEYS = list("pqrstuvwxyz")
DIFFICULTIES = ["easy", "medium", "hard"]


def _build_session(puzzle_key: str, difficulty: str, run_nonce: str) -> dict:
    return {
        "session_id": "mp-test-session",
        "status": "active",
        "owner_username": "owner",
        "guest_username": "guest",
        "seed": f"{difficulty}-seed-alpha",
        "difficulty": difficulty,
        "puzzle_protocol": "v2",
        "run_nonce": run_nonce,
        "current_room": {"x": 1, "y": 1},
        "room_lookup": {
            "1,1": {
                "x": 1,
                "y": 1,
                "puzzle_key": puzzle_key,
                "kind": "N",
            }
        },
        "room_progress": {"1,1": {"puzzle_solved": False, "reward_pickup_collected": False}},
        "room_puzzle_states": {},
        "players": {
            "owner": {
                "role": "owner",
                "room": {"x": 1, "y": 1},
                "position": {"x": 510.0, "y": 510.0, "width": 60.0, "height": 60.0},
            },
            "guest": {
                "role": "guest",
                "room": {"x": 1, "y": 1},
                "position": {"x": 510.0, "y": 510.0, "width": 60.0, "height": 60.0},
            },
        },
    }


class MultiplayerRuntimeTests(unittest.TestCase):
    def test_generation_preflight_for_all_families_and_difficulties(self) -> None:
        for difficulty in DIFFICULTIES:
            for puzzle_key in PUZZLE_KEYS:
                session = _build_session(puzzle_key, difficulty, "nonce-a")
                state = ensure_current_room_puzzle_state_v2(session)
                self.assertEqual(2, state.get("schema_version"))
                self.assertTrue(state.get("preflight_validated"))
                self.assertIn("layout_signature", state)
                self.assertIn("solution_signature", state)
                self.assertTrue(state.get("preflight_trace"))

    def test_preflight_trace_solves_instance(self) -> None:
        for puzzle_key in PUZZLE_KEYS:
            session = _build_session(puzzle_key, "easy", "nonce-b")
            state = ensure_current_room_puzzle_state_v2(session)
            trace = try_solve_v2(copy.deepcopy(state))
            self.assertIsNotNone(trace, f"Expected solve trace for key {puzzle_key}")

    def test_seed_layout_stable_solution_varies_with_run_nonce(self) -> None:
        for puzzle_key in PUZZLE_KEYS:
            session_a = _build_session(puzzle_key, "medium", "nonce-1")
            session_b = _build_session(puzzle_key, "medium", "nonce-2")
            state_a = ensure_current_room_puzzle_state_v2(session_a)
            state_b = ensure_current_room_puzzle_state_v2(session_b)
            self.assertEqual(state_a.get("layout_signature"), state_b.get("layout_signature"))
            self.assertNotEqual(state_a.get("solution_signature"), state_b.get("solution_signature"))

    def test_role_authorization_enforced(self) -> None:
        session = _build_session("p", "easy", "nonce-c")
        _ = ensure_current_room_puzzle_state_v2(session)
        with self.assertRaises(HTTPException):
            apply_puzzle_action_v2(session, "guest", "v2_action", {"cmd": "owner_up"})

    def test_serializer_emits_v2_panel_and_stage_contract(self) -> None:
        session = _build_session("r", "easy", "nonce-panel")
        payload = serialize_current_room_puzzle_v2(session, "owner")
        view = payload["view"]
        self.assertEqual(2, view.get("schema_version"))
        self.assertIn("family_id", view)
        self.assertIn("phase", view)
        self.assertIn("status_code", view)
        self.assertIn("hud", view)
        self.assertIn("actions", view)
        self.assertIn("panel", view)
        self.assertIn("stage", view)
        self.assertIn("stage_elements", view)
        self.assertIn("progress_label", view)
        self.assertIn("progress_value", view)
        self.assertIn("progress_trend", view)
        self.assertIn("stage_visual_profile", view)
        self.assertIn("stage_level", view)
        self.assertIn("failure_code", view)
        self.assertIn("failure_label", view)
        self.assertIn("recovery_text", view)
        self.assertIsInstance(view["panel"], dict)
        self.assertIsInstance(view["stage"], dict)
        self.assertIn("elements", view["stage"])

    def test_failure_language_dictionary_uses_locked_terms(self) -> None:
        self.assertSetEqual(
            set(FAILURE_LANGUAGES["p"].keys()),
            {"phase_drift", "stability_loss", "sync_collapse"},
        )
        self.assertSetEqual(
            set(FAILURE_LANGUAGES["w"].keys()),
            {"containment_leak", "pressure_breach", "routing_fault"},
        )
        self.assertSetEqual(
            set(FAILURE_LANGUAGES["x"].keys()),
            {"archive_conflict", "sequence_corruption", "reconstruction_fault"},
        )

    def test_failure_payload_emits_family_language(self) -> None:
        session = _build_session("p", "easy", "nonce-failure")
        state = ensure_current_room_puzzle_state_v2(session)
        state["runtime"]["a"] = 0
        state["runtime"]["b"] = 0
        state["runtime"]["target_a"] = 9
        state["runtime"]["target_b"] = 9
        apply_puzzle_action_v2(session, "owner", "v2_action", {"cmd": "owner_lock"})
        apply_puzzle_action_v2(session, "guest", "v2_action", {"cmd": "guest_lock"})
        view = serialize_current_room_puzzle_v2(session, "owner")["view"]
        self.assertIn(view["status_code"], {"failed_temporary", "cooldown"})
        self.assertIn(view["failure_code"], {"phase_drift", "stability_loss", "sync_collapse"})
        self.assertTrue(view["failure_label"])
        self.assertTrue(view["recovery_text"])

    def test_stage_one_visual_profile_applies_flagship_simplification(self) -> None:
        tidal_session = _build_session("w", "easy", "nonce-stage")
        tidal_state = ensure_current_room_puzzle_state_v2(tidal_session)
        self.assertEqual(1, tidal_state["stage_level"])
        self.assertEqual("intro", tidal_state["stage_visual_profile"])
        self.assertEqual(1, tidal_state["runtime"]["required"])

        memory_session = _build_session("x", "easy", "nonce-stage")
        memory_state = ensure_current_room_puzzle_state_v2(memory_session)
        self.assertEqual(1, memory_state["stage_level"])
        self.assertEqual("intro", memory_state["stage_visual_profile"])
        self.assertEqual(4, len(memory_state["runtime"]["layers"]))

    def test_tick_determinism(self) -> None:
        session = _build_session("w", "hard", "nonce-d")
        state_a = ensure_current_room_puzzle_state_v2(session)
        state_b = copy.deepcopy(state_a)
        now_a = float(state_a["last_tick"])
        now_b = float(state_b["last_tick"])
        for _ in range(20):
            now_a += 0.125
            now_b += 0.125
            tick_puzzle_state_v2(state_a, now_a, 0.125)
            tick_puzzle_state_v2(state_b, now_b, 0.125)
        self.assertEqual(state_a["runtime"], state_b["runtime"])
        self.assertEqual(state_a["status_code"], state_b["status_code"])


if __name__ == "__main__":
    unittest.main()
