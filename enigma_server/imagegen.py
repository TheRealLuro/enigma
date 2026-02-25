import base64
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FuturesTimeoutError
from functools import lru_cache
import hashlib
import io
import threading
from typing import List, Tuple

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter, ImageOps
import torch

from diffusionengine import get_pipe
from fetchprompt import get_prompt_pack


ROOM_TYPES = {
    "A": [True, False, False, False],
    "B": [False, True, False, False],
    "C": [True, True, False, False],
    "D": [False, False, True, False],
    "E": [True, False, True, False],
    "F": [False, True, True, False],
    "G": [True, True, True, False],
    "H": [False, False, False, True],
    "I": [True, False, False, True],
    "J": [False, True, False, True],
    "K": [True, True, False, True],
    "L": [False, False, True, True],
    "M": [True, False, True, True],
    "N": [False, True, True, True],
    "O": [True, True, True, True],
}

PUZZLE_TYPES = {
    "p": "Pressure Plate Puzzle",
    "q": "Quick Time Reaction Puzzle",
    "r": "Riddle Puzzle",
    "s": "Sequence Memory Puzzle",
    "t": "Tile Rotation Puzzle",
    "u": "Unlock Pattern Puzzle",
    "v": "Valve Flow Puzzle",
    "w": "Weight Balance Puzzle",
    "x": "XOR Logic Puzzle",
    "y": "Yarn / Path Untangle Puzzle",
    "z": "Zone Activation Puzzle",
}

ROOM_INDEX = {key: idx for idx, key in enumerate(ROOM_TYPES.keys())}
PUZZLE_INDEX = {key: idx for idx, key in enumerate(PUZZLE_TYPES.keys())}


# Prompt content is sourced from fetchprompt.py
_PROMPT_PACK = get_prompt_pack()
PROMPT_PACK_NAME = str(_PROMPT_PACK.get("name", "default"))
PROMPT_COMPOSITION_ELITE = _PROMPT_PACK["PROMPT_COMPOSITION_ELITE"]
PROMPT_COLORS_ELITE = _PROMPT_PACK["PROMPT_COLORS_ELITE"]
SCIENCE_CUES_ELITE = _PROMPT_PACK["SCIENCE_CUES_ELITE"]
PROMPT_PAREIDOLIA_ELITE = _PROMPT_PACK["PROMPT_PAREIDOLIA_ELITE"]
PROMPT_NEUROCHEM_ELITE = _PROMPT_PACK["PROMPT_NEUROCHEM_ELITE"]
PROMPT_TEMPORAL_ELITE = _PROMPT_PACK["PROMPT_TEMPORAL_ELITE"]
PROMPT_EDGE_ELITE = _PROMPT_PACK["PROMPT_EDGE_ELITE"]
PROMPT_ARCHETYPES_ELITE = _PROMPT_PACK["PROMPT_ARCHETYPES_ELITE"]
PROMPT_SCALE_ELITE = _PROMPT_PACK["PROMPT_SCALE_ELITE"]
PROMPT_FLOW_RULES = _PROMPT_PACK["PROMPT_FLOW_RULES"]
PROMPT_COMPOSITION_ELITE_DEFINED = _PROMPT_PACK["PROMPT_COMPOSITION_ELITE_DEFINED"]
PROMPT_COLORS_ELITE_DEFINED = _PROMPT_PACK["PROMPT_COLORS_ELITE_DEFINED"]
SCIENCE_CUES_ELITE_DEFINED = _PROMPT_PACK["SCIENCE_CUES_ELITE_DEFINED"]
PROMPT_PAREIDOLIA_ELITE_DEFINED = _PROMPT_PACK["PROMPT_PAREIDOLIA_ELITE_DEFINED"]
PROMPT_NEUROCHEM_ELITE_DEFINED = _PROMPT_PACK["PROMPT_NEUROCHEM_ELITE_DEFINED"]
PROMPT_TEMPORAL_ELITE_DEFINED = _PROMPT_PACK["PROMPT_TEMPORAL_ELITE_DEFINED"]
PROMPT_EDGE_ELITE_DEFINED = _PROMPT_PACK["PROMPT_EDGE_ELITE_DEFINED"]
PROMPT_ARCHETYPES_ELITE_DEFINED = _PROMPT_PACK["PROMPT_ARCHETYPES_ELITE_DEFINED"]
PROMPT_SCALE_ELITE_DEFINED = _PROMPT_PACK["PROMPT_SCALE_ELITE_DEFINED"]
PROMPT_FLOW_RULES_DEFINED = _PROMPT_PACK["PROMPT_FLOW_RULES_DEFINED"]
NEURAL_CAVE_SUBJECT = _PROMPT_PACK["NEURAL_CAVE_SUBJECT"]
NEURAL_CAVE_STRUCTURE = _PROMPT_PACK["NEURAL_CAVE_STRUCTURE"]
NEURAL_CAVE_GLOW = _PROMPT_PACK["NEURAL_CAVE_GLOW"]
NEURAL_CAVE_PAREIDOLIA = _PROMPT_PACK["NEURAL_CAVE_PAREIDOLIA"]
NEURAL_CAVE_MOOD = _PROMPT_PACK["NEURAL_CAVE_MOOD"]
NEURAL_CAVE_SCIENCE = _PROMPT_PACK["NEURAL_CAVE_SCIENCE"]
NEURAL_CAVE_MORPH = _PROMPT_PACK["NEURAL_CAVE_MORPH"]
NEURAL_CAVE_DEPTH = _PROMPT_PACK["NEURAL_CAVE_DEPTH"]
NEURAL_CAVE_ORGANIC_PRESENCE = _PROMPT_PACK["NEURAL_CAVE_ORGANIC_PRESENCE"]
CORE_PROMPTS = _PROMPT_PACK["CORE_PROMPTS"]
ORGANIC_SHORT = _PROMPT_PACK["ORGANIC_SHORT"]
BASE_REFINER_PROMPT = _PROMPT_PACK["BASE_REFINER_PROMPT"]
BASE_NEGATIVE_PROMPT = _PROMPT_PACK["BASE_NEGATIVE_PROMPT"]

MAX_GENERATION_QUEUE = 32
_generation_executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="imagegen")
_pending_jobs_lock = threading.Lock()
_pending_jobs = 0

# Performance/quality balance tuned for SDXL Turbo on modern NVIDIA GPUs.
DIFFUSE_BASE_STEPS = 18
DIFFUSE_REFINER_STEPS = 10
DIFFUSE_CHARACTER_STEPS = 24
DIFFUSE_CARTOON_DETAIL_STEPS = 14
WORK_IMAGE_SIZE = 950
FINAL_IMAGE_SIZE = 2160


def _upscale_to_target(image: Image.Image, target_size: int = 2160) -> Image.Image:
    if image.size == (target_size, target_size):
        return image

    rgb = image.convert("RGB")
    upscaled = rgb.resize((target_size, target_size), resample=Image.Resampling.LANCZOS)
    return upscaled.filter(ImageFilter.DETAIL).filter(ImageFilter.SHARPEN)


def _clamp_uint8(value: float) -> int:
    return max(0, min(255, int(value)))


def _seed_to_int(seed: str) -> int:
    return int(hashlib.sha256(seed.encode("utf-8")).hexdigest()[:16], 16)


def _rng_for(seed: str, salt: int = 0) -> np.random.Generator:
    return np.random.default_rng(_seed_to_int(f"{seed}:{salt}"))


def _pick_prompt_fragment(rng: np.random.Generator, primary: List[str], secondary: List[str], secondary_weight: float = 0.35) -> str:
    if secondary and rng.random() < secondary_weight:
        return secondary[rng.integers(0, len(secondary))]
    return primary[rng.integers(0, len(primary))]


def _build_prompt(seed: str) -> str:
    # Seed-coherent prompt assembly for style stability per seed.
    rng = _rng_for(seed, salt=11)
    _, _, points, grid_w, grid_h = _seed_context(seed)

    mean_x = sum(p[0] for p in points) / max(1, len(points))
    mean_y = sum(p[1] for p in points) / max(1, len(points))
    skew_x = "horizontal flow" if mean_x > (grid_w / 2) else "vertical flow"
    skew_y = "ascending energy" if mean_y > (grid_h / 2) else "descending energy"
    # Keep cave/tunnel structure mandatory and compact so optional traits survive truncation.
    structure_short = _clip_prompt_words(
        NEURAL_CAVE_STRUCTURE[rng.integers(0, len(NEURAL_CAVE_STRUCTURE))],
        max_words=7,
    )
    glow_short = _clip_prompt_words(
        NEURAL_CAVE_GLOW[rng.integers(0, len(NEURAL_CAVE_GLOW))],
        max_words=7,
    )
    core = [
        CORE_PROMPTS[0],
        CORE_PROMPTS[1],
    ]
    is_cartoon_pack = "cartoon" in PROMPT_PACK_NAME
    if not is_cartoon_pack:
        # Keep membrane/default packs tunnel-locked.
        core.append("first-person tunnel interior, clear central passage, receding layered chambers")
    optional = [
        structure_short,
        glow_short,
        NEURAL_CAVE_MORPH[rng.integers(0, len(NEURAL_CAVE_MORPH))],
        NEURAL_CAVE_DEPTH[rng.integers(0, len(NEURAL_CAVE_DEPTH))],
        NEURAL_CAVE_PAREIDOLIA[rng.integers(0, len(NEURAL_CAVE_PAREIDOLIA))],
        NEURAL_CAVE_MOOD[rng.integers(0, len(NEURAL_CAVE_MOOD))],
        NEURAL_CAVE_SCIENCE[rng.integers(0, len(NEURAL_CAVE_SCIENCE))],
        _pick_prompt_fragment(rng, PROMPT_COLORS_ELITE, PROMPT_COLORS_ELITE_DEFINED),
        _pick_prompt_fragment(rng, PROMPT_FLOW_RULES, PROMPT_FLOW_RULES_DEFINED),
        f"{skew_x}, {skew_y}",
        "no architecture, no rough rock texture, no geometric floor",
    ]
    # Effective occasional organic matter (kept subtle).
    if rng.random() < 0.22:
        optional.insert(0, ORGANIC_SHORT[rng.integers(0, len(ORGANIC_SHORT))])
    if rng.random() < 0.5:
        optional.append("vector-like smoothness and retina-sharp clean gradients")

    assembled = list(core)
    for fragment in optional:
        candidate = ", ".join(assembled + [fragment])
        if len(candidate.split()) > 42:
            break
        assembled.append(fragment)

    return ", ".join(assembled)


def _build_character_prompt(seed: str) -> str:
    rng = _rng_for(seed, salt=19)
    subject = _clip_prompt_words(
        NEURAL_CAVE_SUBJECT[rng.integers(0, len(NEURAL_CAVE_SUBJECT))],
        max_words=8,
    )
    mood = _clip_prompt_words(
        NEURAL_CAVE_MOOD[rng.integers(0, len(NEURAL_CAVE_MOOD))],
        max_words=6,
    )
    if "cartoon" in PROMPT_PACK_NAME:
        cues = [
            "1930s rubber hose cartoon style, vintage animation look",
            "thick confident black outlines, clean flat cel fills",
            "single dominant color per character, colored to match scene palette",
            "strong color contrast between living subjects and background environment",
            "high saturation on characters with calmer background tones for clear visual hierarchy",
            "rubber hose limbs, white gloves, big expressive eyes, clean facial layout",
            "coherent anatomy and consistent species design",
            "characters grounded on floor with simple cast shadows and edge lighting",
            "uniform scale by perspective depth, no floating limbs, no melted faces",
            subject,
            mood,
        ]
    else:
        cues = [
            "coherent character anatomy and readable silhouettes",
            "distinct foreground characters with clean outlines and stable proportions",
            "clear facial features and limb structure, no melted linework",
            subject,
            mood,
        ]
    return ", ".join(cues)


def _build_cartoon_detail_prompt(seed: str) -> str:
    rng = _rng_for(seed, salt=101)
    structure = _clip_prompt_words(
        NEURAL_CAVE_STRUCTURE[rng.integers(0, len(NEURAL_CAVE_STRUCTURE))],
        max_words=8,
    )
    return ", ".join(
        [
            "hand-drawn 1930s rubber hose tunnel scene",
            "clean inked contour lines with subtle line wobble",
            "coherent cel-shaded characters and props",
            "toy-like tunnel depth with readable arches and shelves",
            "high detail character expressions and polished linework",
            structure,
        ]
    )


def _clip_prompt_words(text: str, max_words: int = 24) -> str:
    return " ".join(text.split()[:max_words])


@lru_cache(maxsize=2048)
def _seed_context(seed: str):
    rooms = seed.split("-")
    if len(rooms) < 2:
        raise ValueError("Invalid seed format.")

    difficulty = rooms[0]
    room_tokens = tuple(rooms[1:])
    parsed_rooms = []
    all_x = []
    all_y = []

    for room in room_tokens:
        parts = room.split(",")
        x = int(parts[0])
        remainder = parts[1]
        y = int(remainder[:-3])
        rc = remainder[-3]
        pt = remainder[-2]
        rt = remainder[-1]
        parsed_rooms.append((x, y, rc, pt, rt))
        all_x.append(x)
        all_y.append(y)

    grid_w = max(all_x) + 1
    grid_h = max(all_y) + 1
    points = tuple((x, y) for x, y, _, _, _ in parsed_rooms)
    return difficulty, tuple(parsed_rooms), points, grid_w, grid_h


def _parse_seed(seed: str) -> Tuple[str, List[str], int, int]:
    difficulty, parsed_rooms, _, grid_w, grid_h = _seed_context(seed)
    room_tokens = [f"{x},{y}{rc}{pt}{rt}" for x, y, rc, pt, rt in parsed_rooms]
    return difficulty, room_tokens, grid_w, grid_h


def _get_border_params(seed: str, image_size: int) -> Tuple[Tuple[int, int, int], int]:
    difficulty, _, _, grid_w, grid_h = _seed_context(seed)
    border_colors = {
        "easy": (230, 225, 210),   # off white
        "medium": (175, 120, 20),  # dark yellow/orange
        "hard": (120, 20, 20),     # dark red
    }
    border_color = border_colors.get(difficulty, (230, 225, 210))
    total_squares = grid_w * grid_h
    border_size = max(20, min(80, 80 - total_squares // 2))
    border_size = min(border_size, image_size // 4)
    return border_color, border_size


def _wave_distort(img_array: np.ndarray, amp_x: int, amp_y: int, freq: float) -> np.ndarray:
    rows, cols = img_array.shape[:2]
    x_coords, y_coords = np.meshgrid(np.arange(cols), np.arange(rows))

    src_x = (x_coords + amp_x * np.sin(2 * np.pi * y_coords * freq)).astype(np.int32)
    src_y = (y_coords + amp_y * np.cos(2 * np.pi * x_coords * freq)).astype(np.int32)

    src_x = np.clip(src_x, 0, cols - 1)
    src_y = np.clip(src_y, 0, rows - 1)
    return img_array[src_y, src_x]


def _apply_neural_composition(image: Image.Image, seed: str) -> Image.Image:
    # Distributed salience: avoid single focal anchors.
    # Stage 2/3 hook-lock: unresolved near-symmetry + mixed spatial frequencies.
    # Stage 4 trance: depth recession and recursive-like continuity.
    _, _, pts, grid_w, grid_h = _seed_context(seed)

    rng = _rng_for(seed, salt=23)
    mean_x = sum(p[0] for p in pts) / max(1, len(pts))
    mean_y = sum(p[1] for p in pts) / max(1, len(pts))

    arr = np.array(image.convert("RGB"), dtype=np.float32)
    h, w = arr.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    nx = xx / max(1, w - 1)
    ny = yy / max(1, h - 1)

    # Multiple salience islands with balanced intensity.
    salience = np.zeros((h, w), dtype=np.float32)
    island_count = int(rng.integers(4, 9))
    bias_x = float(mean_x / max(1, grid_w - 1)) if grid_w > 1 else 0.5
    bias_y = float(mean_y / max(1, grid_h - 1)) if grid_h > 1 else 0.5
    for _ in range(island_count):
        cx = float(np.clip((rng.uniform(0.08, 0.92) * 0.75) + (bias_x * 0.25), 0.06, 0.94))
        cy = float(np.clip((rng.uniform(0.08, 0.92) * 0.75) + (bias_y * 0.25), 0.06, 0.94))
        sigma = float(rng.uniform(0.04, 0.12))
        d = np.sqrt((nx - cx) ** 2 + (ny - cy) ** 2)
        salience += np.exp(-(d**2) / (2 * sigma**2)).astype(np.float32)
    salience = salience / max(1e-6, float(salience.max()))

    # Directional depth field (avoids center-focused focal bias).
    angle = float(rng.uniform(0.0, 2.0 * np.pi))
    vx = np.cos(angle)
    vy = np.sin(angle)
    directional = ((nx - 0.5) * vx) + ((ny - 0.5) * vy)
    directional = (directional - directional.min()) / max(1e-6, (directional.max() - directional.min()))
    depth_floor = float(rng.uniform(0.70, 0.84))
    depth_ceil = float(rng.uniform(0.96, 1.04))
    depth_field = depth_floor + ((depth_ceil - depth_floor) * directional)

    # Mixed spatial frequencies: preserve detail while carrying low-frequency masses.
    base = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), mode="RGB")
    low_r = float(rng.uniform(4.5, 8.5))
    hi_r = float(rng.uniform(1.0, 2.2))
    low = np.array(base.filter(ImageFilter.GaussianBlur(low_r)), dtype=np.float32)
    hi = arr - np.array(base.filter(ImageFilter.GaussianBlur(hi_r)), dtype=np.float32)
    low_w = float(rng.uniform(0.62, 0.78))
    arr_w = float(rng.uniform(0.22, 0.38))
    hi_w = float(rng.uniform(0.42, 0.68))
    mixed = np.clip((low * low_w) + (arr * arr_w) + (hi * hi_w), 0, 255)

    # 70-80% resolved symmetry tension.
    mirror = np.flip(mixed, axis=1)
    mirror_shift = int(rng.integers(max(1, w // 80), max(2, w // 20)))
    mirror = np.roll(mirror, shift=mirror_shift, axis=1)
    mirror_w = float(rng.uniform(0.14, 0.30))
    mixed = np.clip((mixed * (1.0 - mirror_w)) + (mirror * mirror_w), 0, 255)

    # Depth + distributed salience shaping.
    mixed *= depth_field[:, :, None]
    salience_boost = float(rng.uniform(8.0, 16.0))
    mixed = np.clip(mixed + (salience[:, :, None] * salience_boost), 0, 255)

    out = Image.fromarray(mixed.astype(np.uint8), mode="RGB")
    out = out.filter(ImageFilter.UnsharpMask(radius=1.2, percent=140, threshold=2))
    return out


def _imprint_cave_depth(image: Image.Image, seed: str) -> Image.Image:
    # Force tunnel-like depth so outputs stay cave-like instead of flat lattices.
    rng = np.random.default_rng(_seed_to_int(seed))
    is_cartoon_pack = "cartoon" in PROMPT_PACK_NAME
    arr = np.array(image.convert("RGB"), dtype=np.float32)
    h, w = arr.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    nx = xx / max(1, w - 1)
    ny = yy / max(1, h - 1)

    cx = float(rng.uniform(0.44, 0.58))
    cy = float(rng.uniform(0.54, 0.68))
    if is_cartoon_pack:
        rx = float(rng.uniform(0.24, 0.34))
        ry = float(rng.uniform(0.21, 0.31))
    else:
        # Slightly tighter opening for stronger tunnel read in membrane/default theme.
        rx = float(rng.uniform(0.19, 0.28))
        ry = float(rng.uniform(0.17, 0.25))

    d = np.sqrt(((nx - cx) / rx) ** 2 + ((ny - cy) / ry) ** 2)
    opening = np.exp(-(d**2) * (1.55 if is_cartoon_pack else 2.15)).astype(np.float32)

    edge = np.minimum.reduce([nx, 1.0 - nx, ny, 1.0 - ny])
    edge_vignette = np.clip(edge / 0.23, 0.0, 1.0).astype(np.float32)
    wall_dark = (0.56 + (0.44 * edge_vignette)) if is_cartoon_pack else (0.50 + (0.50 * edge_vignette))
    depth_gain = (0.68 + (0.58 * opening)) if is_cartoon_pack else (0.60 + (0.74 * opening))

    shaped = arr * wall_dark[:, :, None] * depth_gain[:, :, None]

    # Subtle ribbing to hint curved tunnel walls.
    theta = np.arctan2(ny - cy, nx - cx)
    radial = np.sqrt((nx - cx) ** 2 + (ny - cy) ** 2)
    ribs = 0.5 + (0.5 * np.sin((radial * 42.0) + (theta * 2.8) + float(rng.uniform(0.0, 2.0 * np.pi))))
    rib_mask = np.clip(1.10 - d, 0.0, 1.0).astype(np.float32) * (0.10 if is_cartoon_pack else 0.14)
    shaped += (ribs[:, :, None] * rib_mask[:, :, None] * (26.0 if is_cartoon_pack else 32.0))

    shaped = np.clip(shaped, 0, 255)
    out = Image.fromarray(shaped.astype(np.uint8), mode="RGB")
    return out.filter(ImageFilter.UnsharpMask(radius=1.0, percent=96, threshold=2))


def _apply_spectral_grade(image: Image.Image, seed: str) -> Image.Image:
    # Non-deterministic HSV perturbation with seed-layout-informed color direction.
    rng = _rng_for(seed, salt=29)
    _, _, points, grid_w, grid_h = _seed_context(seed)

    mean_x = sum(p[0] for p in points) / max(1, len(points))
    mean_y = sum(p[1] for p in points) / max(1, len(points))
    coord_hue_bias = int(((mean_x / max(1, grid_w - 1)) * 128 + (mean_y / max(1, grid_h - 1)) * 127)) % 256
    hsv = np.array(image.convert("HSV"), dtype=np.int16)
    h = hsv[:, :, 0]
    s = hsv[:, :, 1]
    v = hsv[:, :, 2]

    hue_shift = int((rng.integers(0, 256) + coord_hue_bias + rng.integers(0, 256)) % 256)
    h = (h + hue_shift) % 256

    # Multi-scale hue drift: broad regional variation + micro jitter for full-spectrum spread.
    hh, ww = h.shape
    yy, xx = np.mgrid[0:hh, 0:ww]
    phase_a = float(rng.uniform(0.0, 2.0 * np.pi))
    phase_b = float(rng.uniform(0.0, 2.0 * np.pi))
    regional = (
        np.sin((xx * rng.uniform(0.010, 0.018)) + phase_a)
        + np.cos((yy * rng.uniform(0.010, 0.020)) + phase_b)
    ) * rng.uniform(8.0, 18.0)
    jitter = rng.normal(0.0, 8.0, size=h.shape)
    h = (h + regional.astype(np.int16) + jitter.astype(np.int16)) % 256

    # Stronger saturation/value shaping for vivid spectrum without full clipping.
    s = np.clip((s.astype(np.float32) * 1.62) + 34, 0, 255).astype(np.int16)
    v = np.clip((v.astype(np.float32) * 1.14) + 6, 0, 255).astype(np.int16)

    graded = np.stack([h, s, v], axis=-1).astype(np.uint8)
    return Image.fromarray(graded, mode="HSV").convert("RGB")


def _apply_microdot_texture(image: Image.Image) -> Image.Image:
    # Make color fields/lines feel alive by building them from tiny chromatic dots,
    # stronger near edges to keep forms crisp without drawing explicit outlines.
    rng = np.random.default_rng()
    rgb_img = image.convert("RGB")
    rgb = np.array(rgb_img, dtype=np.uint8)
    h, w = rgb.shape[:2]

    edge = np.array(rgb_img.filter(ImageFilter.FIND_EDGES).convert("L"), dtype=np.float32) / 255.0
    # Lower microdot density to avoid speckled/dotted color fields.
    prob = np.clip(0.01 + (edge * 0.05), 0.01, 0.06)
    mask = rng.random((h, w)) < prob

    hsv = np.array(Image.fromarray(rgb, mode="RGB").convert("HSV"), dtype=np.int16)
    hue_jitter = rng.integers(-3, 4, size=(h, w), dtype=np.int16)
    sat_boost = rng.integers(2, 10, size=(h, w), dtype=np.int16)
    val_jitter = rng.integers(-2, 3, size=(h, w), dtype=np.int16)

    hsv[:, :, 0][mask] = (hsv[:, :, 0][mask] + hue_jitter[mask]) % 256
    hsv[:, :, 1][mask] = np.clip(hsv[:, :, 1][mask] + sat_boost[mask], 0, 255)
    hsv[:, :, 2][mask] = np.clip(hsv[:, :, 2][mask] + val_jitter[mask], 0, 255)

    return Image.fromarray(hsv.astype(np.uint8), mode="HSV").convert("RGB")


def _solidify_color_fields(image: Image.Image) -> Image.Image:
    # Edge-aware color solidification: smooth speckles in flats, preserve structure edges.
    base = image.convert("RGB")
    median = base.filter(ImageFilter.MedianFilter(size=3))
    smooth = base.filter(ImageFilter.GaussianBlur(0.22))

    base_arr = np.array(base, dtype=np.float32)
    m_arr = np.array(median, dtype=np.float32)
    s_arr = np.array(smooth, dtype=np.float32)

    edge = np.array(base.filter(ImageFilter.FIND_EDGES).convert("L"), dtype=np.float32) / 255.0
    edge = np.clip(edge, 0.0, 1.0)
    flat = 1.0 - np.clip(np.power(edge, 0.6) * 1.8, 0.0, 1.0)

    # Stronger smoothing in flat fields, minimal smoothing near edges.
    alpha_m = 0.36 * flat
    alpha_s = 0.14 * flat
    out = (base_arr * (1.0 - alpha_m[:, :, None])) + (m_arr * alpha_m[:, :, None])
    out = (out * (1.0 - alpha_s[:, :, None])) + (s_arr * alpha_s[:, :, None])
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), mode="RGB")


def _apply_cartoon_toon_finish(image: Image.Image) -> Image.Image:
    # Cartoon-only finalizer: smooth clean cel blocks with soft, non-harsh outlines.
    base = image.convert("RGB")
    # Smooth texture noise first.
    smooth = base.filter(ImageFilter.GaussianBlur(0.35))
    # Keep more tonal steps so character features don't collapse into blobs.
    poster = ImageOps.posterize(smooth, bits=6)

    # Very soft outline layer (no hard contour mask/composite).
    edges = poster.filter(ImageFilter.FIND_EDGES).convert("L")
    edge_arr = np.array(edges, dtype=np.float32)
    edge_soft = np.clip((edge_arr - 70.0) / 170.0, 0.0, 1.0)
    edge_soft = np.power(edge_soft, 1.35) * 0.16

    p_arr = np.array(poster, dtype=np.float32)
    # Gently darken near edges instead of drawing thick black lines.
    p_arr = p_arr * (1.0 - edge_soft[:, :, None])
    toon = Image.fromarray(np.clip(p_arr, 0, 255).astype(np.uint8), mode="RGB")
    toon = ImageEnhance.Color(toon).enhance(1.08)
    toon = ImageEnhance.Contrast(toon).enhance(1.05)
    toon = toon.filter(ImageFilter.SMOOTH)
    return toon


def _cartoon_palette_separation(image: Image.Image, seed: str) -> Image.Image:
    # Palette-aware subject/background separation for stronger character pop.
    rng = np.random.default_rng(_seed_to_int(seed))
    hsv = np.array(image.convert("HSV"), dtype=np.float32)
    h = hsv[:, :, 0]
    s = hsv[:, :, 1]
    v = hsv[:, :, 2]
    hh, ww = h.shape
    yy, xx = np.mgrid[0:hh, 0:ww]

    # Broad full-spectrum drift to avoid pack color lock-in.
    phase_a = float(rng.uniform(0.0, 2.0 * np.pi))
    phase_b = float(rng.uniform(0.0, 2.0 * np.pi))
    regional = (
        np.sin((xx * rng.uniform(0.008, 0.016)) + phase_a)
        + np.cos((yy * rng.uniform(0.008, 0.016)) + phase_b)
    ) * rng.uniform(12.0, 24.0)
    h = (h + regional + rng.uniform(0.0, 255.0)) % 256.0

    # Foreground-biased mask (characters/organic subjects): edges + saturation + spatial priors.
    edge = np.array(image.convert("RGB").filter(ImageFilter.FIND_EDGES).convert("L"), dtype=np.float32) / 255.0
    edge = np.array(Image.fromarray((edge * 255).astype(np.uint8), mode="L").filter(ImageFilter.GaussianBlur(1.0)), dtype=np.float32) / 255.0
    sat_n = s / 255.0
    val_n = v / 255.0
    lower_prior = np.clip((yy / max(1, hh - 1) - 0.28) / 0.72, 0.0, 1.0)
    center_prior = 1.0 - np.clip(np.abs((xx / max(1, ww - 1)) - 0.5) / 0.5, 0.0, 1.0)
    spatial = (lower_prior * 0.65) + (center_prior * 0.35)
    fg = np.clip((edge * 1.28) + (sat_n * 0.24) + (val_n * 0.12) + (spatial * 0.30) - 0.52, 0.0, 1.0)
    fg = np.array(Image.fromarray((fg * 255).astype(np.uint8), mode="L").filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255.0

    # Analyze background palette (dominant hue/value/saturation excluding subject mask).
    bg = 1.0 - fg
    bg_w_sum = float(np.sum(bg))
    if bg_w_sum > 1e-6:
        bg_h_rad = (h / 255.0) * (2.0 * np.pi)
        bg_sin = float(np.sum(np.sin(bg_h_rad) * bg) / bg_w_sum)
        bg_cos = float(np.sum(np.cos(bg_h_rad) * bg) / bg_w_sum)
        bg_hue = (np.arctan2(bg_sin, bg_cos) / (2.0 * np.pi)) % 1.0
        bg_hue = bg_hue * 255.0
        bg_sat = float(np.sum(s * bg) / bg_w_sum)
        bg_val = float(np.sum(v * bg) / bg_w_sum)
    else:
        bg_hue = float(np.mean(h))
        bg_sat = float(np.mean(s))
        bg_val = float(np.mean(v))

    # Target subject hue is complementary (high hue contrast) with slight seed jitter.
    subj_hue = (bg_hue + 128.0 + float(rng.uniform(-10.0, 10.0))) % 256.0
    hue_delta = ((subj_hue - h + 128.0) % 256.0) - 128.0
    hue_strength = np.clip((fg * 0.72) + 0.08, 0.0, 0.85)
    h = (h + (hue_delta * hue_strength)) % 256.0

    # Value/saturation contrast: vivid subjects vs calmer background.
    subj_sat_target = float(np.clip(max(bg_sat + 62.0, 178.0), 160.0, 246.0))
    subj_val_target = float(np.clip(bg_val + 48.0 if bg_val < 138.0 else bg_val - 54.0, 92.0, 236.0))
    s = (s * (1.0 - (fg * 0.64))) + (subj_sat_target * (fg * 0.64))
    v = (v * (1.0 - (fg * 0.58))) + (subj_val_target * (fg * 0.58))
    s = np.clip((s * (1.0 - (bg * 0.10))), 0.0, 255.0)
    v = np.clip((v * (1.0 - (bg * 0.06))), 0.0, 255.0)

    # Enforce minimum luminance contrast for immediate subject/background separation.
    contrast_need = np.clip((42.0 - np.abs(v - bg_val)) / 42.0, 0.0, 1.0)
    v = np.clip(v + (fg * contrast_need * 22.0), 0.0, 255.0)

    out = np.stack([h, s, v], axis=-1).astype(np.uint8)
    return Image.fromarray(out, mode="HSV").convert("RGB")


def _refine_cartoon_characters(image: Image.Image) -> Image.Image:
    # Preserve character detail while gently cleaning tiny speckles.
    base = image.convert("RGB")
    median = base.filter(ImageFilter.MedianFilter(size=3))
    b_arr = np.array(base, dtype=np.float32)
    m_arr = np.array(median, dtype=np.float32)
    edge = np.array(base.filter(ImageFilter.FIND_EDGES).convert("L"), dtype=np.float32) / 255.0
    flat = 1.0 - np.clip(np.power(edge, 0.65) * 1.8, 0.0, 1.0)

    # Light cleanup in flat fields only.
    alpha = 0.16 * flat
    out = (b_arr * (1.0 - alpha[:, :, None])) + (m_arr * alpha[:, :, None])
    cleaned = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), mode="RGB")
    cleaned = cleaned.filter(ImageFilter.UnsharpMask(radius=0.8, percent=86, threshold=1))
    cleaned = ImageEnhance.Contrast(cleaned).enhance(1.05)
    return ImageEnhance.Color(cleaned).enhance(1.05)


def _cartoon_degrain(image: Image.Image) -> Image.Image:
    # Remove dotty chroma while preserving character edges.
    base = image.convert("RGB")
    med = base.filter(ImageFilter.MedianFilter(size=3))
    soft = base.filter(ImageFilter.GaussianBlur(0.18))
    b_arr = np.array(base, dtype=np.float32)
    m_arr = np.array(med, dtype=np.float32)
    s_arr = np.array(soft, dtype=np.float32)
    edge = np.array(base.filter(ImageFilter.FIND_EDGES).convert("L"), dtype=np.float32) / 255.0
    flat = 1.0 - np.clip(np.power(edge, 0.72) * 1.9, 0.0, 1.0)
    a_m = 0.32 * flat
    a_s = 0.14 * flat
    out = (b_arr * (1.0 - a_m[:, :, None])) + (m_arr * a_m[:, :, None])
    out = (out * (1.0 - a_s[:, :, None])) + (s_arr * a_s[:, :, None])
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), mode="RGB")


def _lift_deep_blacks(image: Image.Image) -> Image.Image:
    # Keep occasional dark contrast, but prevent large crushed-black regions.
    arr = np.array(image.convert("RGB"), dtype=np.float32)
    luma = (0.2126 * arr[:, :, 0]) + (0.7152 * arr[:, :, 1]) + (0.0722 * arr[:, :, 2])

    # Softly lift deep shadows; stronger lift in very dark regions.
    very_dark = np.clip((26.0 - luma) / 26.0, 0.0, 1.0)
    dark = np.clip((50.0 - luma) / 50.0, 0.0, 1.0)
    lift = (very_dark * 32.0) + (dark * 10.0)

    arr = np.clip(arr + lift[:, :, None], 0, 255)
    # Gentle gamma lift to reduce large near-black regions.
    arr = np.clip(arr / 255.0, 0.0, 1.0)
    arr = np.power(arr, 0.93)
    arr = np.clip(arr * 255.0, 0, 255)
    return Image.fromarray(arr.astype(np.uint8), mode="RGB")


def _apply_liquid_glass_finish(image: Image.Image, border_size: int, seed: str, intensity: float = 0.44) -> Image.Image:
    # Unified polished-glass finish across the inner artwork area (smooth, not muddy).
    base = image.convert("RGB")
    base_arr = np.array(base, dtype=np.float32)
    h, w = base_arr.shape[:2]
    rng = _rng_for(seed, salt=41)
    yy, xx = np.mgrid[0:h, 0:w]

    # Inner frame (inside border) defines where glass applies.
    x0 = int(border_size)
    y0 = int(border_size)
    x1 = int(max(x0 + 1, w - border_size))
    y1 = int(max(y0 + 1, h - border_size))
    inside = ((xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)).astype(np.float32)

    # Lighter multi-scale diffusion to keep crisp structure while smoothing surfaces.
    soft1 = np.array(base.filter(ImageFilter.GaussianBlur(0.75)), dtype=np.float32)
    soft2 = np.array(base.filter(ImageFilter.GaussianBlur(1.6)), dtype=np.float32)
    soft3 = np.array(base.filter(ImageFilter.GaussianBlur(2.6)), dtype=np.float32)

    # Low-frequency scattering field.
    scatter = rng.normal(0.0, 1.0, size=(h, w)).astype(np.float32)
    scatter = Image.fromarray(np.clip((scatter * 24.0) + 128.0, 0, 255).astype(np.uint8), mode="L")
    scatter = scatter.filter(ImageFilter.GaussianBlur(10.0))
    scatter = np.array(scatter, dtype=np.float32) / 255.0
    scatter = (scatter - 0.5) * 8.0

    frosted = (base_arr * 0.58) + (soft1 * 0.24) + (soft2 * 0.12) + (soft3 * 0.06)
    frosted = frosted + scatter[:, :, None]

    # Mild translucency and very light contrast compression.
    frosted = frosted * 0.985 + 3.0
    frosted = 127.5 + ((frosted - 127.5) * 0.97)
    frosted = np.clip(frosted, 0, 255)

    # Blend only inside the border frame; keep border color crisp.
    alpha = (float(np.clip(intensity, 0.0, 0.75)) * inside)[:, :, None]
    out = (base_arr * (1.0 - alpha)) + (frosted * alpha)
    out_img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), mode="RGB")
    out_img = out_img.filter(ImageFilter.UnsharpMask(radius=0.9, percent=76, threshold=1))
    out_img = ImageEnhance.Contrast(out_img).enhance(1.04)
    return ImageEnhance.Brightness(out_img).enhance(1.00)


def _apply_emboss_pop(image: Image.Image) -> Image.Image:
    # Color-preserving pseudo-emboss from luminance gradients (semi-3D relief look).
    rgb = np.array(image.convert("RGB"), dtype=np.float32)
    luma = (0.2126 * rgb[:, :, 0]) + (0.7152 * rgb[:, :, 1]) + (0.0722 * rgb[:, :, 2])

    # Sobel-like gradients using shifts (no extra deps).
    gx = (
        np.roll(luma, -1, axis=1) - np.roll(luma, 1, axis=1)
        + 0.5 * (np.roll(np.roll(luma, -1, axis=0), -1, axis=1) - np.roll(np.roll(luma, -1, axis=0), 1, axis=1))
        + 0.5 * (np.roll(np.roll(luma, 1, axis=0), -1, axis=1) - np.roll(np.roll(luma, 1, axis=0), 1, axis=1))
    )
    gy = (
        np.roll(luma, -1, axis=0) - np.roll(luma, 1, axis=0)
        + 0.5 * (np.roll(np.roll(luma, -1, axis=1), -1, axis=0) - np.roll(np.roll(luma, -1, axis=1), 1, axis=0))
        + 0.5 * (np.roll(np.roll(luma, 1, axis=1), -1, axis=0) - np.roll(np.roll(luma, 1, axis=1), 1, axis=0))
    )

    # Build normals and light for relief shading.
    nx = -gx
    ny = -gy
    nz = np.full_like(nx, 72.0)
    norm = np.sqrt((nx * nx) + (ny * ny) + (nz * nz)) + 1e-6
    nx /= norm
    ny /= norm
    nz /= norm

    lx, ly, lz = 0.45, -0.45, 0.78
    shading = (nx * lx) + (ny * ly) + (nz * lz)
    shading = np.clip(shading, -1.0, 1.0)

    # Stronger relief with controlled dynamic range.
    gain = 1.0 + (shading * 0.44)
    gain = np.clip(gain, 0.58, 1.52)
    embossed = np.clip(rgb * gain[:, :, None], 0, 255).astype(np.uint8)

    # Color-preserving edge pop in HSV, strongest where gradient magnitude is high.
    grad_mag = np.sqrt((gx * gx) + (gy * gy))
    grad_mag = grad_mag / max(1e-6, float(grad_mag.max()))
    edge_w = np.clip(np.power(grad_mag, 0.62) * 0.60, 0.0, 0.60).astype(np.float32)

    hsv = np.array(Image.fromarray(embossed, mode="RGB").convert("HSV"), dtype=np.float32)
    hsv[:, :, 1] = np.clip(hsv[:, :, 1] * (1.0 + (edge_w * 1.05)), 0, 255)
    hsv[:, :, 2] = np.clip(hsv[:, :, 2] * (1.0 + (edge_w * 0.56)), 0, 255)
    out = Image.fromarray(hsv.astype(np.uint8), mode="HSV").convert("RGB")
    return out


def _final_clean_smooth(image: Image.Image) -> Image.Image:
    # Last-pass polish: very light, edge-aware smoothing.
    base = image.convert("RGB")
    soft = base.filter(ImageFilter.GaussianBlur(0.28))
    b_arr = np.array(base, dtype=np.float32)
    s_arr = np.array(soft, dtype=np.float32)
    edge = np.array(base.filter(ImageFilter.FIND_EDGES).convert("L"), dtype=np.float32) / 255.0
    flat = 1.0 - np.clip(np.power(edge, 0.7) * 1.7, 0.0, 1.0)
    alpha = 0.10 * flat
    out = (b_arr * (1.0 - alpha[:, :, None])) + (s_arr * alpha[:, :, None])
    blended = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), mode="RGB")
    blended = blended.filter(ImageFilter.UnsharpMask(radius=0.85, percent=72, threshold=1))
    return ImageEnhance.Contrast(blended).enhance(1.03)


def _build_base_image(seed: str, image_size: int = 1080) -> Image.Image:
    _, parsed_rooms, _, grid_w, grid_h = _seed_context(seed)
    rng = _rng_for(seed, salt=53)
    is_cartoon_pack = "cartoon" in PROMPT_PACK_NAME

    _, border_size = _get_border_params(seed, image_size)
    inner_size = image_size - (border_size * 2)
    cell_w = max(1, inner_size // grid_w)
    cell_h = max(1, inner_size // grid_h)

    # Build aggressive multi-source noise so diffusion has more latent detail to transform.
    if is_cartoon_pack:
        uniform_weight = float(rng.uniform(0.28, 0.40))
        gaussian_weight = float(rng.uniform(0.50, 0.62))
        sp_weight = max(0.03, 1.0 - (uniform_weight + gaussian_weight))
        gauss_sigma = float(rng.uniform(34.0, 52.0))
    else:
        uniform_weight = float(rng.uniform(0.42, 0.62))
        gaussian_weight = float(rng.uniform(0.23, 0.43))
        sp_weight = max(0.05, 1.0 - (uniform_weight + gaussian_weight))
        gauss_sigma = float(rng.uniform(58.0, 88.0))

    uniform_noise = rng.integers(0, 256, size=(inner_size, inner_size, 3), dtype=np.uint8).astype(np.int16)
    gaussian_noise = rng.normal(128, gauss_sigma, size=(inner_size, inner_size, 3)).astype(np.int16)
    pepper_prob = float(rng.uniform(0.08, 0.18))
    salt_pepper = rng.choice(
        np.array([0, 255], dtype=np.int16),
        size=(inner_size, inner_size, 3),
        p=[1.0 - pepper_prob, pepper_prob],
    )
    inner = np.clip(
        (uniform_noise * uniform_weight) + (gaussian_noise * gaussian_weight) + (salt_pepper * sp_weight),
        0,
        255,
    ).astype(np.uint8)

    for x, y, rc, pt, rt in parsed_rooms:

        if rt == "R":
            color = (100, 0, 100)
        elif rt == "S":
            color = (0, 150, 0)
        elif rt == "F":
            color = (150, 0, 0)
        else:
            x_norm = x / (grid_w - 1) if grid_w > 1 else 0
            y_norm = y / (grid_h - 1) if grid_h > 1 else 0
            rc_i = ROOM_INDEX.get(rc, 0)
            pt_i = PUZZLE_INDEX.get(pt, 0)
            noise = rng.integers(140, 256, size=3)

            color = (
                _clamp_uint8(noise[0] + (rc_i * 6) - (pt_i * 4) + x_norm * 22 - y_norm * 14),
                _clamp_uint8(noise[1] - (rc_i * 5) + (pt_i * 6) - x_norm * 18 + y_norm * 12),
                _clamp_uint8(noise[2] + (rc_i * 3) + (pt_i * 5) - x_norm * 12 - y_norm * 8),
            )

        x0 = x * cell_w
        y0 = y * cell_h
        x1 = min(inner_size, x0 + cell_w)
        y1 = min(inner_size, y0 + cell_h)

        # Keep strong noise texture while lightly biasing each room toward its color.
        patch = inner[y0:y1, x0:x1].astype(np.float32)
        tint = np.array(color, dtype=np.float32)[None, None, :]
        blended = (patch * 0.82) + (tint * 0.18)
        inner[y0:y1, x0:x1] = np.clip(blended, 0, 255).astype(np.uint8)

    if is_cartoon_pack:
        inner = _wave_distort(
            inner,
            amp_x=int(rng.integers(8, 18)),
            amp_y=int(rng.integers(5, 14)),
            freq=float(rng.uniform(0.004, 0.008)),
        )
        inner = _wave_distort(
            inner,
            amp_x=int(rng.integers(4, 10)),
            amp_y=int(rng.integers(8, 16)),
            freq=float(rng.uniform(0.008, 0.013)),
        )
    else:
        inner = _wave_distort(
            inner,
            amp_x=int(rng.integers(18, 36)),
            amp_y=int(rng.integers(8, 20)),
            freq=float(rng.uniform(0.004, 0.009)),
        )
        inner = _wave_distort(
            inner,
            amp_x=int(rng.integers(8, 20)),
            amp_y=int(rng.integers(16, 30)),
            freq=float(rng.uniform(0.009, 0.016)),
        )
        inner = _wave_distort(
            inner,
            amp_x=int(rng.integers(4, 12)),
            amp_y=int(rng.integers(4, 12)),
            freq=float(rng.uniform(0.03, 0.06)),
        )
    inner_img = (
        Image.fromarray(inner)
        .filter(ImageFilter.GaussianBlur(0.7))
        .filter(ImageFilter.DETAIL)
        .filter(ImageFilter.EDGE_ENHANCE_MORE)
        .filter(ImageFilter.SHARPEN)
    )
    inner_img = _apply_neural_composition(inner_img, seed=seed)
    inner_img = _imprint_cave_depth(inner_img, seed=seed)

    return inner_img


def _build_layout_condition(image: Image.Image, seed: str, is_cartoon_pack: bool) -> Image.Image:
    # Light structural conditioning for perspective and focal hierarchy.
    arr = np.array(image.convert("RGB"), dtype=np.float32)
    h, w = arr.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    nx = xx / max(1, w - 1)
    ny = yy / max(1, h - 1)
    rng = _rng_for(seed, salt=61)

    van_x = float(rng.uniform(0.42, 0.58))
    van_y = float(rng.uniform(0.52, 0.74))
    d = np.sqrt(((nx - van_x) / 0.56) ** 2 + ((ny - van_y) / 0.48) ** 2)
    center_pull = np.exp(-(d**2) * (1.5 if is_cartoon_pack else 1.2)).astype(np.float32)

    # Preserve negative space around the top side walls for readability.
    side_mass = np.clip(np.abs(nx - 0.5) / 0.5, 0.0, 1.0)
    top_open = np.clip(1.0 - (ny / 0.52), 0.0, 1.0)
    flatten = (side_mass * top_open) * (0.12 if is_cartoon_pack else 0.06)

    luma_boost = (center_pull * (24.0 if is_cartoon_pack else 14.0)) - (flatten * 36.0)
    shaped = np.clip(arr + luma_boost[:, :, None], 0, 255)
    out = Image.fromarray(shaped.astype(np.uint8), mode="RGB")
    return out.filter(ImageFilter.UnsharpMask(radius=0.95, percent=105, threshold=2))


def _soft_focus_mask(size: Tuple[int, int], seed: str) -> Image.Image:
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w]
    rng = _rng_for(seed, salt=71)
    cx = float(rng.uniform(0.44, 0.56) * w)
    cy = float(rng.uniform(0.66, 0.80) * h)
    rx = float(rng.uniform(0.24, 0.34) * w)
    ry = float(rng.uniform(0.16, 0.25) * h)
    d = np.sqrt(((xx - cx) / max(1.0, rx)) ** 2 + ((yy - cy) / max(1.0, ry)) ** 2)
    core = np.clip(1.0 - d, 0.0, 1.0)
    soft = np.power(core, 0.72)
    mask = Image.fromarray(np.clip(soft * 255.0, 0, 255).astype(np.uint8), mode="L")
    return mask.filter(ImageFilter.GaussianBlur(22.0))


def _refine_character_region(
    pipe,
    image: Image.Image,
    seed: str,
    prompt: str,
    negative_prompt: str,
) -> Image.Image:
    # Secondary local pass: spend budget where characters usually appear.
    w, h = image.size
    x0 = int(w * 0.20)
    y0 = int(h * 0.46)
    x1 = int(w * 0.84)
    y1 = int(h * 0.95)
    crop = image.crop((x0, y0, x1, y1)).convert("RGB")
    crop = crop.resize((512, 512), resample=Image.Resampling.LANCZOS)

    has_cuda = torch.cuda.is_available()
    generator = torch.Generator(device="cuda" if has_cuda else "cpu")
    generator.manual_seed(_seed_to_int(f"{seed}:char"))
    refined_crop = pipe(
        prompt=_clip_prompt_words(prompt, max_words=34),
        negative_prompt=_clip_prompt_words(negative_prompt, max_words=34),
        image=crop,
        strength=0.34,
        guidance_scale=6.9,
        num_inference_steps=DIFFUSE_CHARACTER_STEPS,
        generator=generator,
    ).images[0].convert("RGB")

    refined_crop = refined_crop.resize((x1 - x0, y1 - y0), resample=Image.Resampling.LANCZOS)
    mask = _soft_focus_mask((w, h), seed=seed)
    layer = Image.new("RGB", (w, h))
    layer.paste(refined_crop, (x0, y0))
    return Image.composite(layer, image.convert("RGB"), mask)


def diffuse_abstract(image: Image.Image, seed: str) -> Image.Image:
    pipe = get_pipe()

    prompt = _build_prompt(seed)
    character_prompt = _build_character_prompt(seed)
    detail_prompt = _build_cartoon_detail_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    # Keep CLIP token length safely below SDXL's 77-token limit.
    prompt = _clip_prompt_words(prompt, max_words=40)
    refiner_prompt = _clip_prompt_words(refiner_prompt, max_words=22)
    negative_prompt = _clip_prompt_words(negative_prompt, max_words=34)

    is_cartoon_pack = "cartoon" in PROMPT_PACK_NAME
    base_steps = DIFFUSE_BASE_STEPS + (14 if is_cartoon_pack else 2)
    refiner_steps = DIFFUSE_REFINER_STEPS + (9 if is_cartoon_pack else 0)

    has_cuda = torch.cuda.is_available()
    generator = torch.Generator(device="cuda" if has_cuda else "cpu")
    generator.manual_seed(_seed_to_int(f"{seed}:scene"))

    conditioned = _build_layout_condition(image, seed=seed, is_cartoon_pack=is_cartoon_pack)

    # Stage 1: scene render with composition conditioning.
    base = pipe(
        prompt=prompt,
        negative_prompt=negative_prompt,
        image=conditioned.convert("RGB"),
        strength=0.58 if is_cartoon_pack else 0.52,
        guidance_scale=6.0 if is_cartoon_pack else 5.4,
        num_inference_steps=base_steps,
        generator=generator,
    ).images[0]

    # Stage 2: local character coherence pass (cartoon only).
    if is_cartoon_pack:
        character_negative = (
            f"{negative_prompt}, inconsistent character sizes, white unshaded characters, floating figures, melted faces, "
            "mismatched art styles, uncolored characters, clipping geometry, broken anatomy, blob figures, palette breaks, "
            "realistic shading, thin outlines, painterly gradients, malformed eyes, deformed hands"
        )
        base = _refine_character_region(
            pipe=pipe,
            image=base,
            seed=seed,
            prompt=character_prompt,
            negative_prompt=character_negative,
        )
        # Stage 2b: hand-drawn detail pass for cleaner ink-like structure.
        generator.manual_seed(_seed_to_int(f"{seed}:toon_detail"))
        base = pipe(
            prompt=_clip_prompt_words(detail_prompt, max_words=34),
            negative_prompt=negative_prompt,
            image=base.convert("RGB"),
            strength=0.16,
            guidance_scale=5.8,
            num_inference_steps=DIFFUSE_CARTOON_DETAIL_STEPS,
            generator=generator,
        ).images[0]

    # Stage 3: global low-denoise refinement for line/lighting stability.
    generator.manual_seed(_seed_to_int(f"{seed}:refine"))
    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=negative_prompt,
        image=base,
        strength=0.20 if is_cartoon_pack else 0.20,
        guidance_scale=5.0 if is_cartoon_pack else 5.0,
        num_inference_steps=refiner_steps,
        generator=generator,
    ).images[0]

    # Keep generation at base resolution for speed; upscale happens after full composition.
    result = result.convert("RGB")
    if is_cartoon_pack:
        # Cartoon pack: keep solid fills and readable forms; avoid heavy global blur.
        result = _cartoon_palette_separation(result, seed=seed)
        result = _cartoon_degrain(result)
        result = ImageEnhance.Color(result).enhance(1.15)
        result = ImageEnhance.Contrast(result).enhance(1.06)
        result = _apply_cartoon_toon_finish(result)
        result = _refine_cartoon_characters(result)
        result = ImageEnhance.Sharpness(result).enhance(1.06)
    else:
        result = _apply_spectral_grade(result, seed=seed)
        # Keep non-cartoon outputs cleaner and less dotted.
        result = _solidify_color_fields(result)
        result = _lift_deep_blacks(result)
        result = result.filter(ImageFilter.GaussianBlur(0.26))
        result = ImageEnhance.Color(result).enhance(1.36)
        result = ImageEnhance.Contrast(result).enhance(1.18)
        result = ImageEnhance.Sharpness(result).enhance(1.02)

    return result


def generate_map_image(seed: str, use_diffusion: bool = True) -> Image.Image:
    is_cartoon_pack = "cartoon" in PROMPT_PACK_NAME
    base_image_size = 1080 if is_cartoon_pack else WORK_IMAGE_SIZE
    target_size = FINAL_IMAGE_SIZE

    inner_image = _build_base_image(seed, image_size=base_image_size)
    if use_diffusion:
        inner_image = diffuse_abstract(inner_image, seed)
    else:
        inner_image = inner_image.convert("RGB")

    border_color, border_size = _get_border_params(seed, base_image_size)

    # Compose at base resolution for speed.
    canvas = Image.new("RGB", (base_image_size, base_image_size), border_color)
    inner_target_size = base_image_size - (border_size * 2)
    inner_resized = inner_image.resize((inner_target_size, inner_target_size), resample=Image.Resampling.LANCZOS)
    inner_resized = inner_resized.filter(
        ImageFilter.UnsharpMask(
            radius=1.4 if is_cartoon_pack else 1.8,
            percent=140 if is_cartoon_pack else 180,
            threshold=2,
        )
    )
    canvas.paste(inner_resized, (border_size, border_size))
    # Pack-specific finishing strengths.
    embossed = _apply_emboss_pop(canvas)
    canvas = Image.blend(canvas, embossed, alpha=0.14 if is_cartoon_pack else 0.28)
    canvas = _apply_liquid_glass_finish(
        canvas,
        border_size=border_size,
        seed=seed,
        intensity=0.22 if is_cartoon_pack else 0.44,
    )
    # Final upscale only after border + post effects.
    canvas = _upscale_to_target(canvas, target_size=target_size)
    if is_cartoon_pack:
        canvas = canvas.filter(ImageFilter.UnsharpMask(radius=0.9, percent=88, threshold=1))
        canvas = ImageEnhance.Contrast(canvas).enhance(1.03)
    else:
        canvas = _final_clean_smooth(canvas)
    return canvas


def _generate_map_image_b64_now(seed: str, use_diffusion: bool = True) -> str:
    image = generate_map_image(seed=seed, use_diffusion=use_diffusion)
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return base64.b64encode(buffer.getvalue()).decode("ascii")


def _generate_map_image_payload_now(seed: str, use_diffusion: bool = True) -> dict:
    map_image = _generate_map_image_b64_now(seed=seed, use_diffusion=use_diffusion)
    return {
        "map_image": map_image,
        "theme": PROMPT_PACK_NAME,
    }


def _queued_generate(seed: str, use_diffusion: bool) -> str:
    global _pending_jobs
    try:
        return _generate_map_image_b64_now(seed=seed, use_diffusion=use_diffusion)
    finally:
        with _pending_jobs_lock:
            _pending_jobs -= 1


def _queued_generate_payload(seed: str, use_diffusion: bool) -> dict:
    global _pending_jobs
    try:
        return _generate_map_image_payload_now(seed=seed, use_diffusion=use_diffusion)
    finally:
        with _pending_jobs_lock:
            _pending_jobs -= 1


def generate_map_image_b64(
    seed: str,
    use_diffusion: bool = True,
    timeout_seconds: float = 600.0,
) -> str:
    global _pending_jobs
    with _pending_jobs_lock:
        if _pending_jobs >= MAX_GENERATION_QUEUE:
            raise RuntimeError("Image generation queue is full. Try again shortly.")
        _pending_jobs += 1

    future = _generation_executor.submit(_queued_generate, seed, use_diffusion)
    try:
        return future.result(timeout=timeout_seconds)
    except FuturesTimeoutError:
        future.cancel()
        raise RuntimeError("Image generation timed out in queue.")


def generate_map_image_payload(
    seed: str,
    use_diffusion: bool = True,
    timeout_seconds: float = 600.0,
) -> dict:
    global _pending_jobs
    with _pending_jobs_lock:
        if _pending_jobs >= MAX_GENERATION_QUEUE:
            raise RuntimeError("Image generation queue is full. Try again shortly.")
        _pending_jobs += 1

    future = _generation_executor.submit(_queued_generate_payload, seed, use_diffusion)
    try:
        return future.result(timeout=timeout_seconds)
    except FuturesTimeoutError:
        future.cancel()
        raise RuntimeError("Image generation timed out in queue.")
