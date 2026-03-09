from __future__ import annotations


CO_OP_PUZZLE_KEY_ORDER = ["p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z"]

_FAMILIES = {
    "p": {
        "family_id": "split_signal",
        "name": "Split Signal",
        "core_mechanic": "Asymmetric waveform alignment",
        "mechanic_type": "Split control",
        "accent_color": "#06B6D4",
    },
    "q": {
        "family_id": "pressure_exchange",
        "name": "Pressure Exchange",
        "core_mechanic": "Divided valve control",
        "mechanic_type": "Shared simulation",
        "accent_color": "#A855F7",
    },
    "r": {
        "family_id": "bridge_builder",
        "name": "Bridge Builder",
        "core_mechanic": "Split construction zones",
        "mechanic_type": "Joint topology",
        "accent_color": "#F97316",
    },
    "s": {
        "family_id": "mirror_minds",
        "name": "Mirror Minds",
        "core_mechanic": "Asymmetric memory reconstruction",
        "mechanic_type": "Deduction grid",
        "accent_color": "#EC4899",
    },
    "t": {
        "family_id": "flood_control",
        "name": "Flood Control",
        "core_mechanic": "Gates versus pumps",
        "mechanic_type": "Real-time balancing",
        "accent_color": "#34D399",
    },
    "u": {
        "family_id": "cipher_relay",
        "name": "Cipher Relay",
        "core_mechanic": "Sequential shared decode",
        "mechanic_type": "Relay dependency",
        "accent_color": "#818CF8",
    },
    "v": {
        "family_id": "gravity_tandem",
        "name": "Gravity Tandem",
        "core_mechanic": "Direction plus thrust coupling",
        "mechanic_type": "Split simulation input",
        "accent_color": "#FB923C",
    },
    "w": {
        "family_id": "tidal_lock",
        "name": "Tidal Lock",
        "core_mechanic": "Synchronized moving window catches",
        "mechanic_type": "Timing cooperation",
        "accent_color": "#38BDF8",
    },
    "x": {
        "family_id": "strata_shift",
        "name": "Strata Shift",
        "core_mechanic": "Interleaved layer alignment",
        "mechanic_type": "Role-interleaved transforms",
        "accent_color": "#C084FC",
    },
    "y": {
        "family_id": "echo_sync",
        "name": "Echo Sync",
        "core_mechanic": "Timing and frequency resonance",
        "mechanic_type": "Temporal + spectral sync",
        "accent_color": "#4ADE80",
    },
    "z": {
        "family_id": "temporal_weave",
        "name": "Temporal Weave",
        "core_mechanic": "Cross-timeline dependency grid",
        "mechanic_type": "Temporal logic",
        "accent_color": "#F87171",
    },
}

_DESCRIPTIONS = {
    "easy": {
        "p": "Owner tunes frequency while guest tunes amplitude. Lock both channels inside tolerance together.",
        "q": "Owner opens intake valves and guest releases pressure. Hold every tank in the safe band together.",
        "r": "Each player controls half the bridge segments. Activate the shared stable path.",
        "s": "Each player reconstructs mirrored memory cells from partial truth. Align both halves.",
        "t": "Owner shapes flood flow with gates while guest drains with pumps. Protect all critical cells.",
        "u": "Owner controls shift and guest controls column relay order. Commit matching decode settings.",
        "v": "Owner sets heading, guest sets thrust. Launch with both settings aligned to the target orbit.",
        "w": "Catch moving tidal windows together. Both players must pulse inside the same lock window.",
        "x": "Owner and guest adjust alternating strata layers. Align every layer to its target offset.",
        "y": "Guest tunes resonance frequency while owner fires pulse timing. Build synchronized resonances.",
        "z": "Past and present grids are linked. Fill both timelines without triggering paradox resets.",
    },
    "medium": {
        "p": "Signal tolerance narrows and lock discipline tightens.",
        "q": "More tanks and stronger drift require tighter communication.",
        "r": "More beam nodes and decoy toggles increase shared planning depth.",
        "s": "Larger mirrored memory arrays require cleaner pattern language.",
        "t": "Flood dynamics accelerate and safe margins shrink.",
        "u": "Relay decode adds stricter lock ordering and tighter commit windows.",
        "v": "Trajectory target tolerance tightens and launch mistakes reset progress.",
        "w": "More catches are required with narrower synchronization tolerance.",
        "x": "Layer count increases and alignment tolerance tightens.",
        "y": "Resonance windows tighten and required streak depth increases.",
        "z": "Temporal links multiply and contradiction recovery is stricter.",
    },
    "hard": {
        "p": "Near-exact lock with strict desync penalties.",
        "q": "High drift and minimal tolerance; overflow triggers fast rollback.",
        "r": "Dense bridge topology with near-valid traps.",
        "s": "Large mirrored arrays with little error margin.",
        "t": "Fast flood pressure and strict failure recovery.",
        "u": "Strict lock sequence with low tolerance for decode mismatch.",
        "v": "Precise launch synchronization with minimal target tolerance.",
        "w": "Frame-tight capture windows and higher streak requirements.",
        "x": "Maximum strata density and strict alignment checks.",
        "y": "Strict timing/frequency resonance without slack.",
        "z": "High-density weave with strict paradox discipline.",
    },
}


def _build_catalog() -> dict[str, dict[str, dict[str, object]]]:
    catalog: dict[str, dict[str, dict[str, object]]] = {}
    for difficulty, descriptions in _DESCRIPTIONS.items():
        tier: dict[str, dict[str, object]] = {}
        for key in CO_OP_PUZZLE_KEY_ORDER:
            family = _FAMILIES[key]
            tier[key] = {
                "schema_version": 2,
                "family_id": family["family_id"],
                "name": family["name"],
                "description": descriptions[key],
                "core_mechanic": family["core_mechanic"],
                "mechanic_type": family["mechanic_type"],
                "accent_color": family["accent_color"],
                "difficulty_tuning": {
                    "easy": "full feedback, wider tolerance",
                    "medium": "partial information, tighter tolerance",
                    "hard": "strict timing and minimal tolerance",
                },
            }
        catalog[difficulty] = tier
    return catalog


CO_OP_PUZZLE_CATALOG_V2 = _build_catalog()

# In-place upgrade: keep legacy export names pointing at the same updated catalog.
CO_OP_PUZZLE_CATALOG = CO_OP_PUZZLE_CATALOG_V2
CO_OP_PUZZLE_CATALOG_V1 = CO_OP_PUZZLE_CATALOG_V2
