import base64
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FuturesTimeoutError
from contextlib import contextmanager
from functools import lru_cache
import hashlib
import io
import threading
from typing import Any, List, Tuple

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter, ImageOps

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


_prompt_pack_context = threading.local()


def _get_current_prompt_pack() -> dict:
    pack = getattr(_prompt_pack_context, "pack", None)
    return pack if pack is not None else get_prompt_pack()


@contextmanager
def _prompt_pack_scope(pack: dict | None = None):
    previous = getattr(_prompt_pack_context, "pack", None)
    _prompt_pack_context.pack = pack if pack is not None else get_prompt_pack()
    try:
        yield _prompt_pack_context.pack
    finally:
        if previous is None:
            try:
                delattr(_prompt_pack_context, "pack")
            except AttributeError:
                pass
        else:
            _prompt_pack_context.pack = previous


class _PromptPackValueProxy:
    def __init__(self, key: str):
        self._key = key

    def _value(self) -> Any:
        return _get_current_prompt_pack()[self._key]

    def __getitem__(self, item):
        return self._value()[item]

    def __iter__(self):
        return iter(self._value())

    def __len__(self):
        return len(self._value())

    def __contains__(self, item):
        return item in self._value()

    def __str__(self):
        return str(self._value())

    def __repr__(self):
        return repr(self._value())


def _coerce_prompt_text(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, _PromptPackValueProxy):
        value = value._value()
    return str(value)


def _current_pack_name() -> str:
    return _coerce_prompt_text(PROMPT_PACK_NAME)


# Prompt content is sourced from fetchprompt.py, but the active pack is chosen per generation.
PROMPT_PACK_NAME = _PromptPackValueProxy("name")
PROMPT_COMPOSITION_ELITE = _PromptPackValueProxy("PROMPT_COMPOSITION_ELITE")
PROMPT_COLORS_ELITE = _PromptPackValueProxy("PROMPT_COLORS_ELITE")
SCIENCE_CUES_ELITE = _PromptPackValueProxy("SCIENCE_CUES_ELITE")
PROMPT_PAREIDOLIA_ELITE = _PromptPackValueProxy("PROMPT_PAREIDOLIA_ELITE")
PROMPT_NEUROCHEM_ELITE = _PromptPackValueProxy("PROMPT_NEUROCHEM_ELITE")
PROMPT_TEMPORAL_ELITE = _PromptPackValueProxy("PROMPT_TEMPORAL_ELITE")
PROMPT_EDGE_ELITE = _PromptPackValueProxy("PROMPT_EDGE_ELITE")
PROMPT_ARCHETYPES_ELITE = _PromptPackValueProxy("PROMPT_ARCHETYPES_ELITE")
PROMPT_SCALE_ELITE = _PromptPackValueProxy("PROMPT_SCALE_ELITE")
PROMPT_FLOW_RULES = _PromptPackValueProxy("PROMPT_FLOW_RULES")
PROMPT_COMPOSITION_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_COMPOSITION_ELITE_DEFINED")
PROMPT_COLORS_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_COLORS_ELITE_DEFINED")
SCIENCE_CUES_ELITE_DEFINED = _PromptPackValueProxy("SCIENCE_CUES_ELITE_DEFINED")
PROMPT_PAREIDOLIA_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_PAREIDOLIA_ELITE_DEFINED")
PROMPT_NEUROCHEM_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_NEUROCHEM_ELITE_DEFINED")
PROMPT_TEMPORAL_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_TEMPORAL_ELITE_DEFINED")
PROMPT_EDGE_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_EDGE_ELITE_DEFINED")
PROMPT_ARCHETYPES_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_ARCHETYPES_ELITE_DEFINED")
PROMPT_SCALE_ELITE_DEFINED = _PromptPackValueProxy("PROMPT_SCALE_ELITE_DEFINED")
PROMPT_FLOW_RULES_DEFINED = _PromptPackValueProxy("PROMPT_FLOW_RULES_DEFINED")
NEURAL_CAVE_SUBJECT = _PromptPackValueProxy("NEURAL_CAVE_SUBJECT")
NEURAL_CAVE_STRUCTURE = _PromptPackValueProxy("NEURAL_CAVE_STRUCTURE")
NEURAL_CAVE_GLOW = _PromptPackValueProxy("NEURAL_CAVE_GLOW")
NEURAL_CAVE_PAREIDOLIA = _PromptPackValueProxy("NEURAL_CAVE_PAREIDOLIA")
NEURAL_CAVE_MOOD = _PromptPackValueProxy("NEURAL_CAVE_MOOD")
NEURAL_CAVE_SCIENCE = _PromptPackValueProxy("NEURAL_CAVE_SCIENCE")
NEURAL_CAVE_MORPH = _PromptPackValueProxy("NEURAL_CAVE_MORPH")
NEURAL_CAVE_DEPTH = _PromptPackValueProxy("NEURAL_CAVE_DEPTH")
NEURAL_CAVE_ORGANIC_PRESENCE = _PromptPackValueProxy("NEURAL_CAVE_ORGANIC_PRESENCE")
CORE_PROMPTS = _PromptPackValueProxy("CORE_PROMPTS")
ORGANIC_SHORT = _PromptPackValueProxy("ORGANIC_SHORT")
BASE_REFINER_PROMPT = _PromptPackValueProxy("BASE_REFINER_PROMPT")
BASE_NEGATIVE_PROMPT = _PromptPackValueProxy("BASE_NEGATIVE_PROMPT")

MAX_GENERATION_QUEUE = 32
_generation_executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="imagegen")
_pending_jobs_lock = threading.Lock()
_pending_jobs = 0

# Performance/quality balance tuned for SDXL Turbo on modern NVIDIA GPUs.
DIFFUSE_BASE_STEPS = 24
DIFFUSE_REFINER_STEPS = 10
DIFFUSE_CARTOON_BASE_STEPS = 38
DIFFUSE_CARTOON_REFINER_STEPS = 16
DIFFUSE_CARTOON_CHARACTER_STEPS = 22
DIFFUSE_CARTOON_DETAIL_STEPS = 14
DIFFUSE_DUNGEON_BASE_STEPS = 36
DIFFUSE_DUNGEON_REFINER_STEPS = 16
WORK_IMAGE_SIZE = 1080
FINAL_IMAGE_SIZE = 2160

# Conservative prompt word caps to stay below SDXL CLIP 77-token limit.
CLIP_MAIN_WORDS = 28
CLIP_REFINER_WORDS = 14
CLIP_NEGATIVE_WORDS = 24
CLIP_DETAIL_WORDS = 24
CLIP_LOCAL_WORDS = 24


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


def _pick_prompt_fragment(rng: np.random.Generator, primary: List[str], secondary: List[str], secondary_weight: float = 0.35) -> str:
    if secondary and rng.random() < secondary_weight:
        return secondary[rng.integers(0, len(secondary))]
    return primary[rng.integers(0, len(primary))]


def _build_prompt(seed: str) -> str:
    # Non-deterministic style choices each run, while still steering from seed layout features.
    rng = np.random.default_rng()
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
    if rng.random() < 0.18:
        optional.insert(0, ORGANIC_SHORT[rng.integers(0, len(ORGANIC_SHORT))])
    if rng.random() < 0.5:
        optional.append("vector-like smoothness and retina-sharp clean gradients")

    assembled = list(core)
    for fragment in optional:
        candidate = ", ".join(assembled + [fragment])
        if len(candidate.split()) > 40:
            break
        assembled.append(fragment)

    return ", ".join(assembled)


def _clip_prompt_words(text: str, max_words: int = 24) -> str:
    normalized = _coerce_prompt_text(text)
    return " ".join(normalized.split()[:max_words])


def _clip_prompt_safe(text: str, max_words: int, max_chars: int = 220) -> str:
    # Conservative cap for SDXL CLIP context (prevents >77 token warnings).
    clipped = _clip_prompt_words(text, max_words=max_words)
    if len(clipped) > max_chars:
        clipped = clipped[:max_chars]
        if " " in clipped:
            clipped = clipped.rsplit(" ", 1)[0]
    return clipped.strip(" ,")


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

    rng = np.random.default_rng()
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
    arr = np.array(image.convert("RGB"), dtype=np.float32)
    h, w = arr.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    nx = xx / max(1, w - 1)
    ny = yy / max(1, h - 1)

    cx = float(rng.uniform(0.44, 0.58))
    cy = float(rng.uniform(0.54, 0.68))
    rx = float(rng.uniform(0.23, 0.34))
    ry = float(rng.uniform(0.20, 0.31))

    d = np.sqrt(((nx - cx) / rx) ** 2 + ((ny - cy) / ry) ** 2)
    opening = np.exp(-(d**2) * 1.7).astype(np.float32)

    edge = np.minimum.reduce([nx, 1.0 - nx, ny, 1.0 - ny])
    edge_vignette = np.clip(edge / 0.23, 0.0, 1.0).astype(np.float32)
    wall_dark = 0.56 + (0.44 * edge_vignette)
    depth_gain = 0.68 + (0.58 * opening)

    shaped = arr * wall_dark[:, :, None] * depth_gain[:, :, None]

    # Subtle ribbing to hint curved tunnel walls.
    theta = np.arctan2(ny - cy, nx - cx)
    radial = np.sqrt((nx - cx) ** 2 + (ny - cy) ** 2)
    ribs = 0.5 + (0.5 * np.sin((radial * 42.0) + (theta * 2.8) + float(rng.uniform(0.0, 2.0 * np.pi))))
    rib_mask = np.clip(1.10 - d, 0.0, 1.0).astype(np.float32) * 0.10
    shaped += (ribs[:, :, None] * rib_mask[:, :, None] * 26.0)

    shaped = np.clip(shaped, 0, 255)
    out = Image.fromarray(shaped.astype(np.uint8), mode="RGB")
    return out.filter(ImageFilter.UnsharpMask(radius=1.0, percent=96, threshold=2))


def _apply_spectral_grade(image: Image.Image, seed: str) -> Image.Image:
    # Non-deterministic HSV perturbation with seed-layout-informed color direction.
    rng = np.random.default_rng()
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
    # Full-spectrum cartoon palette with gentle foreground/background hue separation.
    rng = np.random.default_rng(_seed_to_int(seed))
    hsv = np.array(image.convert("HSV"), dtype=np.float32)
    h = hsv[:, :, 0]
    s = hsv[:, :, 1]
    v = hsv[:, :, 2]
    hh, ww = h.shape
    yy, xx = np.mgrid[0:hh, 0:ww]

    # Broad full-spectrum drift (avoid green/blue bias).
    phase_a = float(rng.uniform(0.0, 2.0 * np.pi))
    phase_b = float(rng.uniform(0.0, 2.0 * np.pi))
    regional = (
        np.sin((xx * rng.uniform(0.008, 0.016)) + phase_a)
        + np.cos((yy * rng.uniform(0.008, 0.016)) + phase_b)
    ) * rng.uniform(12.0, 24.0)
    h = (h + regional + rng.uniform(0.0, 255.0)) % 256.0

    # Foreground-biased mask (characters/props): edges + saturation + lower-frame prior.
    edge = np.array(image.convert("RGB").filter(ImageFilter.FIND_EDGES).convert("L"), dtype=np.float32) / 255.0
    edge = np.array(Image.fromarray((edge * 255).astype(np.uint8), mode="L").filter(ImageFilter.GaussianBlur(1.0)), dtype=np.float32) / 255.0
    sat_n = s / 255.0
    val_n = v / 255.0
    lower_prior = np.clip((yy / max(1, hh - 1) - 0.28) / 0.72, 0.0, 1.0)
    center_prior = 1.0 - np.clip(np.abs((xx / max(1, ww - 1)) - 0.5) / 0.5, 0.0, 1.0)
    spatial = (lower_prior * 0.65) + (center_prior * 0.35)
    fg = np.clip((edge * 1.28) + (sat_n * 0.24) + (val_n * 0.12) + (spatial * 0.30) - 0.52, 0.0, 1.0)
    fg = np.array(Image.fromarray((fg * 255).astype(np.uint8), mode="L").filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255.0

    # Separate character hues from environment hues.
    fg_shift = float(rng.choice([52.0, 68.0, 84.0, 96.0]))
    bg_shift = float(rng.choice([-22.0, -16.0, -10.0, 8.0]))
    h = (h + (fg * fg_shift) + ((1.0 - fg) * bg_shift)) % 256.0

    # Keep colors solid and vivid.
    s = np.clip((s * 1.18) + 18.0 + (fg * 18.0), 0.0, 255.0)
    v = np.clip((v * 1.05) + (fg * 10.0), 0.0, 255.0)

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


def _apply_liquid_glass_finish(
    image: Image.Image,
    border_size: int,
    seed: str | None = None,
    intensity: float = 0.44,
) -> Image.Image:
    # Unified polished-glass finish across the inner artwork area (smooth, not muddy).
    base = image.convert("RGB")
    base_arr = np.array(base, dtype=np.float32)
    h, w = base_arr.shape[:2]
    rng = np.random.default_rng(_seed_to_int(seed) if seed else None)
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
    rng = np.random.default_rng()

    _, border_size = _get_border_params(seed, image_size)
    inner_size = image_size - (border_size * 2)
    cell_w = max(1, inner_size // grid_w)
    cell_h = max(1, inner_size // grid_h)

    # Build aggressive multi-source noise so diffusion has more latent detail to transform.
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


def _build_cartoon_character_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    return ", ".join(
        [
            "1930s rubber hose cartoon style, coherent character anatomy",
            "thick black outlines, clean cel shading, stable facial features",
            "grounded characters with cast shadows and perspective-correct scale",
            NEURAL_CAVE_SUBJECT[rng.integers(0, len(NEURAL_CAVE_SUBJECT))],
            NEURAL_CAVE_MOOD[rng.integers(0, len(NEURAL_CAVE_MOOD))],
        ]
    )


def _build_cartoon_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    return ", ".join(
        [
            "hand-drawn vintage cartoon line quality",
            "clean contour edges and refined mascot details",
            "toy-tunnel set dressing with readable props and depth",
            NEURAL_CAVE_STRUCTURE[rng.integers(0, len(NEURAL_CAVE_STRUCTURE))],
        ]
    )


def _build_dungeon_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "frequent torch sconces, candles, and lanterns casting warm directional light and long shadows",
        "multiple iron-banded wooden doors in side alcoves and passage turns",
        "skeleton remains and bone piles near wall recesses and corners",
        "spider webs and occasional tiny spiders in dark upper corners",
        "mossy wet stones, ancient chains, and worn masonry details",
    ]
    return ", ".join(
        [
            "realistic medieval dungeon tunnel, ominous and mysterious atmosphere",
            "low-key cinematic lighting driven only by torches, candles, and lanterns with deep shadows and warm falloff",
            "stone vaults, rough masonry, damp textures, coherent perspective depth",
            "clean realism with controlled texture noise and crisp structural edges",
            accents[rng.integers(0, len(accents))],
        ]
    )


def _build_dungeon_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "realistic medieval dungeon tunnel with heavy stone masonry and vaulted arches",
        "first-person descent through a fortress tunnel undercroft with carved stone ribs and alcoves",
        "catacomb corridor tunnel with weathered blocks, buttressed walls, and deep perspective",
    ]
    props = [
        "iron-banded wooden doors recessed into side walls",
        "skeleton remains near wall niches and floor edges",
        "torch sconces, candle clusters, and iron lanterns casting warm directional light and long shadows",
        "spider webs with occasional tiny spiders in vaulted corners",
        "chains, iron hardware, and worn stone relief details",
    ]
    return ", ".join(
        [
            anchors[rng.integers(0, len(anchors))],
            props[rng.integers(0, len(props))],
            props[rng.integers(0, len(props))],
            "dark ominous atmosphere, realistic textures, torch-candle-lantern lit mood only, restrained color, no neon",
            "clear architectural tunnel readability with coherent wall geometry",
        ]
    )


def _soft_focus_mask(size: Tuple[int, int]) -> Image.Image:
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w]
    cx = 0.52 * w
    cy = 0.73 * h
    rx = 0.30 * w
    ry = 0.23 * h
    d = np.sqrt(((xx - cx) / max(1.0, rx)) ** 2 + ((yy - cy) / max(1.0, ry)) ** 2)
    core = np.clip(1.0 - d, 0.0, 1.0)
    soft = np.power(core, 0.75)
    mask = Image.fromarray(np.clip(soft * 255.0, 0, 255).astype(np.uint8), mode="L")
    return mask.filter(ImageFilter.GaussianBlur(20.0))


def _refine_cartoon_character_region(
    pipe,
    image: Image.Image,
    prompt: str,
    negative_prompt: str,
) -> Image.Image:
    w, h = image.size
    x0 = int(w * 0.20)
    y0 = int(h * 0.44)
    x1 = int(w * 0.84)
    y1 = int(h * 0.95)
    crop = image.crop((x0, y0, x1, y1)).convert("RGB")
    crop = crop.resize((512, 512), resample=Image.Resampling.LANCZOS)
    refined_crop = pipe(
        prompt=_clip_prompt_safe(prompt, max_words=CLIP_LOCAL_WORDS, max_chars=170),
        negative_prompt=_clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170),
        image=crop,
        strength=0.34,
        guidance_scale=6.8,
        num_inference_steps=DIFFUSE_CARTOON_CHARACTER_STEPS,
    ).images[0].convert("RGB")
    refined_crop = refined_crop.resize((x1 - x0, y1 - y0), resample=Image.Resampling.LANCZOS)
    mask = _soft_focus_mask((w, h))
    layer = Image.new("RGB", (w, h))
    layer.paste(refined_crop, (x0, y0))
    return Image.composite(layer, image.convert("RGB"), mask)


def _diffuse_membrane_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    # Keep this function identical to the membrane behavior you stabilized.
    prompt = _build_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(refiner_prompt, max_words=CLIP_REFINER_WORDS, max_chars=140)
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=negative_prompt,
        image=image.convert("RGB"),
        strength=0.60,
        guidance_scale=6.0,
        num_inference_steps=DIFFUSE_BASE_STEPS,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=negative_prompt,
        image=base,
        strength=0.20,
        guidance_scale=5.2,
        num_inference_steps=DIFFUSE_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _apply_spectral_grade(result, seed=seed)
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = result.filter(ImageFilter.GaussianBlur(0.34))
    result = ImageEnhance.Color(result).enhance(1.36)
    result = ImageEnhance.Contrast(result).enhance(1.18)
    result = ImageEnhance.Sharpness(result).enhance(1.02)
    return result


def _diffuse_cartoon_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    character_prompt = _build_cartoon_character_prompt(seed)
    detail_prompt = _build_cartoon_detail_prompt(seed)
    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(refiner_prompt, max_words=CLIP_REFINER_WORDS, max_chars=140)
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=negative_prompt,
        image=image.convert("RGB"),
        strength=0.58,
        guidance_scale=6.0,
        num_inference_steps=DIFFUSE_CARTOON_BASE_STEPS,
    ).images[0]

    character_negative = (
        f"{negative_prompt}, inconsistent character sizes, white unshaded characters, floating figures, melted faces, "
        "mismatched art styles, uncolored characters, clipping geometry, broken anatomy, blob figures, palette breaks, "
        "realistic shading, thin outlines, painterly gradients, malformed eyes, deformed hands"
    )
    base = _refine_cartoon_character_region(
        pipe=pipe,
        image=base,
        prompt=character_prompt,
        negative_prompt=character_negative,
    )

    base = pipe(
        prompt=_clip_prompt_safe(detail_prompt, max_words=CLIP_DETAIL_WORDS, max_chars=170),
        negative_prompt=negative_prompt,
        image=base.convert("RGB"),
        strength=0.16,
        guidance_scale=5.8,
        num_inference_steps=DIFFUSE_CARTOON_DETAIL_STEPS,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=negative_prompt,
        image=base,
        strength=0.20,
        guidance_scale=5.0,
        num_inference_steps=DIFFUSE_CARTOON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _cartoon_palette_separation(result, seed=seed)
    result = _cartoon_degrain(result)
    result = ImageEnhance.Color(result).enhance(1.20)
    result = ImageEnhance.Contrast(result).enhance(1.08)
    result = _apply_cartoon_toon_finish(result)
    result = _refine_cartoon_characters(result)
    result = ImageEnhance.Sharpness(result).enhance(1.02)
    return result


def _diffuse_dungeon_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_dungeon_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_dungeon_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(refiner_prompt, max_words=CLIP_REFINER_WORDS, max_chars=140)
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(detail_prompt, max_words=CLIP_DETAIL_WORDS, max_chars=170)

    dungeon_negative = _clip_prompt_safe((
        f"{negative_prompt}, cartoon style, toy plastic look, neon psychedelic colors, flat cel shading, "
        "clean modern architecture, bright daylight, skylight, sunlight, moonlight, window light, outdoor light, "
        "open sky, sci-fi corridor, low contrast haze, big spiders, massive spiders"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=dungeon_negative,
        image=image.convert("RGB"),
        strength=0.58,
        guidance_scale=6.6,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=dungeon_negative,
        image=base.convert("RGB"),
        strength=0.16,
        guidance_scale=6.0,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=dungeon_negative,
        image=detail,
        strength=0.20,
        guidance_scale=5.0,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    # Realistic dark grading: restrained color, deep contrast, sharp structure.
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = result.filter(ImageFilter.GaussianBlur(0.12))
    result = ImageEnhance.Color(result).enhance(0.88)
    result = ImageEnhance.Contrast(result).enhance(1.24)
    result = ImageEnhance.Brightness(result).enhance(0.95)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=96, threshold=2))
    return result


def _build_sewer_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "realistic brick sewer tunnel extending deep into near-total darkness with low arched ceiling and vanishing perspective",
        "first-person descent through a long decaying brick sewer corridor with stagnant water and corroded pipe-lined walls",
        "extensive underground sewer passage with aged mortared brickwork, grate openings, and pitch-black far tunnel depth",
    ]
    props = [
        "misshapen and broken corroded pipes snaking erratically along both side walls with rust bloom at joints",
        "stagnant puddles and grate water surfaces reflecting toxic green ambient light against dark wet ground",
        "scattered garbage items — wrappers, cans, and bottles — identifiable by bright red, yellow, and green packaging",
        "crumbling mortar joints, water staining, and mineral deposit efflorescence across aged brick surfaces",
        "low ground depressions pooling putrid water with faint flow visible through floor grate openings",
    ]
    return ", ".join(
        [
            anchors[rng.integers(0, len(anchors))],
            props[rng.integers(0, len(props))],
            props[rng.integers(0, len(props))],
            "eerie foreboding atmosphere, dim sourceless ambient light fading to near-total black at tunnel depth, no living creatures or organic life present",
            "clear brick tunnel architectural readability with coherent wall geometry and deep perspective recession",
        ]
    )

def _build_sewer_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "misshapen and broken corroded pipes snaking along both side walls with heavy rust bloom and joint leakage staining",
        "stagnant puddles and grate water surfaces with toxic green reflective sheen pooling in ground depressions",
        "scattered garbage items — crushed cans, wrappers, and bottles — with bright isolated red, yellow, and green packaging",
        "crumbling mortar joints, mineral deposit efflorescence, and long moisture stain streaks down aged brick faces",
        "floor grate openings with putrid water visible flowing beneath and low mist collecting above wet ground surface",
    ]
    return ", ".join(
        [
            "realistic brick sewer tunnel, eerie and foreboding atmosphere",
            "dim sourceless ambient lighting fading to near-total pitch black at tunnel depth with toxic green reflections on water surfaces only",
            "low arched brick vaults, corroded pipes, wet cracked stone floor, coherent deep perspective recession",
            "clean realism with controlled texture detail and crisp brick edge definition",
            accents[rng.integers(0, len(accents))],
        ]
    )


def _diffuse_sewer_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_sewer_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_sewer_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(f"{refiner_prompt}, puddles, flowing water, tunnel archways, halls, water, sewer, grates, exit, entrance, tunnel perspective, first person perspective, realism, realistic, river, pathway, hidden faces, realistic textures", max_words=CLIP_REFINER_WORDS, max_chars=140)
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(f"{detail_prompt}, small rats, realistic rats", max_words=CLIP_DETAIL_WORDS, max_chars=170)

    sewer_negative = _clip_prompt_safe((
        f"{negative_prompt}, cartoon style, toy plastic look, neon psychedelic colors, flat cel shading, "
        "clean modern architecture, bright daylight, skylight, sunlight, moonlight, window light, outdoor light, "
        "open sky, sci-fi corridor, low contrast haze, big spiders, massive spiders, unreal, solid wall picture"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=sewer_negative,
        image=image.convert("RGB"),
        strength=0.58,
        guidance_scale=6.6,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=sewer_negative,
        image=base.convert("RGB"),
        strength=0.16,
        guidance_scale=6.0,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=sewer_negative,
        image=detail,
        strength=0.20,
        guidance_scale=5.0,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _apply_spectral_grade(result, seed=seed)
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = result.filter(ImageFilter.GaussianBlur(0.12))
    result = ImageEnhance.Color(result).enhance(0.88)
    result = ImageEnhance.Contrast(result).enhance(1.24)
    result = ImageEnhance.Brightness(result).enhance(0.95)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=96, threshold=2))
    return result


def _build_hedge_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "ground-level first-person view inside a vibrant hedge tunnel corridor with tall trimmed green hedges rising on both sides",
        "eye-level perspective standing inside a sunlit hedge tunnel path with hedge walls towering left and right and sky visible above",
        "walking-level view through a hedge tunnel passage with tall square green hedge walls flanking both sides and path ahead",
    ]
    props = [
        "small scattered stones and sticks along the bright grass path floor",
        "bright blue sky with soft white clouds visible above the hedge tops from ground level",
        "junction ahead where the hedge path splits into multiple directions at eye level",
        "neatly trimmed flat hedge tops visible against open sky when looking upward from path level",
        "dappled natural sunlight casting soft shadows along the grass floor from tall hedge walls",
    ]
    return ", ".join(
        [
            anchors[rng.integers(0, len(anchors))],
            props[rng.integers(0, len(props))],
            props[rng.integers(0, len(props))],
            "ground-level first-person camera, NOT aerial, NOT top-down, NOT birds-eye, eye-level walking perspective only",
            "bright calming atmosphere, natural daylight, vibrant greens and sky blue, no darkness or fear",
        ]
    )

def _build_hedge_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "small scattered light gray stones and pale brown sticks resting naturally along the bright grass floor path",
        "bright blue sky with soft white clouds visible above the hedge tops from ground-level perspective",
        "hedge path junction ahead where the corridor splits into multiple directions as seen from eye level",
        "clean geometric flat-topped hedge walls casting soft midday shadows onto the bright grass floor below",
        "vibrant saturated green hedge walls rising tall on both sides with crisp trimmed edges framing the sky above",
    ]
    return ", ".join(
        [
            "realistic vibrant hedge tunnel interior, calming and intriguing atmosphere",
            "ground-level first-person walking perspective, eye-level camera, NOT top-down, NOT aerial, NOT birds-eye view",
            "tall neatly trimmed square green hedges rising on both sides, bright grass floor, open sky above, coherent forward path depth",
            "bright natural daylight, clean realism, crisp hedge edge definition",
            accents[rng.integers(0, len(accents))],
        ]
    )


def _diffuse_hedge_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_hedge_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_hedge_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(f"{refiner_prompt}, First-person perspective inside a large, photorealistic hedge tunnel. Tall, dense green hedges rise at least 10–12 feet high on both sides,"
        "tightly trimmed but slightly organic and imperfect. Narrow dirt and gravel pathway winding forward, subtle footprints and scattered leaves on the ground. Soft natural sunlight filtering through small gaps in the hedge tops,"
        "creating dappled shadows across the path. Slight depth of field, cinematic lens feel (35mm), ultra-detailed textures on leaves and branches, realistic lighting and shadow behavior, physically accurate materials, high dynamic range,"
        "No visible sky except faint light bleeding through. Slight atmospheric haze in the distance to add depth. Realistic scale and proportions, grounded human eye level perspective,"
        "Subtle environmental storytelling — maybe a faint fork in the path ahead or a partially obscured dead end,"
        "Photorealistic, 8k detail, global illumination, ray-traced lighting, sharp focus, immersive, natural color grading, no stylization, no fantasy elements.first person, first person perspective, hedge pathway, hedge tunnel, tunnel, pathway, walkway, hall, clear walkway, walkway surrounded by bushes, realism, realistic, paved walkway", max_words=CLIP_REFINER_WORDS, max_chars=140)
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(f"{detail_prompt}, first person view, detailed leaves, bush looks real, real looking hedge, real looking bushes, small flowers, detailed grass walkway, grass path, archway in front of point of view,", max_words=CLIP_DETAIL_WORDS, max_chars=170)

    hedge_negative = _clip_prompt_safe((
        f"{negative_prompt}, cartoon style, toy plastic look, neon psychedelic colors, flat cel shading, wall in the way, wall, obstructed path, path blocked, smudged details"
        "clean modern architecture, window light"
        "open sky, sci-fi corridor, low contrast haze, big spiders, massive spiders, unreal, solid wall picture, sky down view, third person view"
       "aerial view, top-down view, birds-eye view, overhead perspective, drone shot, map view, overhead camera, tunnel in the view, top down view, structure on the walkway, mini maze in view, maze in view, smudged details, smudged leaves, wall in front of the tunnel "
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=hedge_negative,
        image=image.convert("RGB"),
        strength=0.58,
        guidance_scale=6.6,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=hedge_negative,
        image=base.convert("RGB"),
        strength=0.16,
        guidance_scale=6.0,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=hedge_negative,
        image=detail,
        strength=0.20,
        guidance_scale=5.0,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = ImageEnhance.Color(result).enhance(0.88)
    result = ImageEnhance.Contrast(result).enhance(1.24)
    result = ImageEnhance.Brightness(result).enhance(0.95)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=96, threshold=2))
    return result

def _build_haunted_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        " extremely faint translucent white-gray haze vaguely resembling a human silhouette barely visible in the far darkness, "
"ghost presence suggested not shown, near-zero opacity ethereal mist in human shape at hallway depth, "
"subtle luminous haze only, not a solid figure, not a clear ghost, absorbed almost entirely by surrounding darkness, end tables bearing melting candles and candelabras casting faint warm amber glow and long soft shadows down the hallway",
        "faded wallpaper with visible aged repeating pattern, peeling at corners and edges revealing bare plaster beneath",
        "thick cobwebs draped in upper wall corners and across old door frames with fine layered strand detail",
        "old dark framed portraits and landscape paintings mounted on the wall between closed wooden doors",
        "extremely faint translucent ghost figure barely perceptible at the very end of the hallway, nearly consumed by darkness",
    ]
    return ", ".join(
        [
            "standing inside an extremely dark narrow haunted house hallway, faded peeling wallpaper close on both sides, worn wooden floor underfoot, closed wooden doors receding ahead",
            "ground-level first-person eye-level interior camera looking down the hallway, NOT aerial NOT top-down NOT exterior",
            "candlelight and candelabras as sole light source — faint warm amber glow only, darkness dominates, deep muted purples and faded ochre wood",
            "no living creatures, no insects, no animals, no humans, no water, no plants — abandoned and lifeless",
            accents[rng.integers(0, len(accents))],
        ]
    )

def _build_haunted_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "ground-level first-person eye-level view down an extremely dark narrow haunted house hallway with faded wallpaper and closed wooden doors",
        "immersive interior corridor perspective inside a haunted house with peeling wallpaper, cobwebbed walls, and candle-lit end tables receding into darkness",
        "thin dark haunted hallway extending forward with closed wooden doors, old framed paintings, and faint candlelight barely illuminating the space",
    ]
    props = [
        "end tables with melting candles and candelabras emitting faint warm amber light and long shadows",
        "faded peeling wallpaper with visible aged pattern and bare plaster showing beneath peeled sections",
        "thick cobwebs draped across upper wall corners and door frames with fine layered strand detail",
        "old dark framed paintings on the walls barely legible in low candlelight",
        "extremely faint barely visible translucent ghost shape at the very far end of the hallway",
    ]
    return ", ".join(
        [
            anchors[rng.integers(0, len(anchors))],
            props[rng.integers(0, len(props))],
            props[rng.integers(0, len(props))],
            "deep muted purples and faded ochre wood tones, candlelight as sole light source, darkness dominates",
            "ground-level first-person interior eye-level camera only, NOT aerial NOT top-down, no living creatures, no water, no plants",
        ]
    )

def _diffuse_haunted_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_haunted_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_haunted_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(
        f"{refiner_prompt}, "
        "first-person eye-level perspective standing inside an extremely dark narrow haunted house hallway, "
        "faded peeling wallpaper close on both sides, worn wooden floorboards underfoot, closed wooden doors receding ahead, "
        "end tables with melting candles and candelabras as sole light source casting faint warm amber glow and long shadows, "
        "cobwebs in upper corners and across door frames, old dark framed paintings on walls, "
        "deep muted purples and near-black shadow dominating, faded ochre wood tones on doors and floor, "
        "extremely faint barely visible translucent ghost shape at the very far end of the hallway, "
        "photorealistic, 8k detail, global illumination, ray-traced candlelight, sharp focus, "
        "immersive cinematic 35mm lens feel, natural shadow behavior, high dynamic range, "
        "no daylight, no windows, no electric light, candlelight only, darkness dominates",
        max_words=CLIP_REFINER_WORDS, max_chars=140
    )
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(
        f"{detail_prompt}, "
        "first person view, detailed faded wallpaper texture with peeling edges, fine cobweb strand detail in corners, "
        "tarnished brass door hardware, worn floorboard grain, dripping candle wax detail, "
        "faint ghost shape at hallway end near-transparent, dark framed painting detail on walls, some blood, tiny blood spots",
        max_words=CLIP_DETAIL_WORDS, max_chars=170
    )

    haunted_negative = _clip_prompt_safe((
        f"{negative_prompt}, "
        "aerial view, top-down view, birds-eye view, overhead perspective, drone shot, map view, "
        "exterior house view, outside the building, outdoor scene, "
        "open doors, visible room interiors, daylight, windows, sunlight, bright lighting, even illumination, "
        "electric lighting, modern fixtures, clean undamaged wallpaper, new surfaces, well-maintained interior, "
        "explicit clear ghost figure, fully visible apparition, cartoon ghost, solid white ghost, "
        "insects, rodents, animals, humans, characters, living creatures, "
        "plants, nature, water, organic life, moss, roots, "
        "bright colors, neon, saturated palette, fantasy magic effects, "
        "cartoon style, illustrated look, painterly rendering, stylized art, "
        "gore,  third person view, wide open space, huge cobwebs"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=haunted_negative,
        image=image.convert("RGB"),
        strength=0.62,
        guidance_scale=5.5,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    base = base.filter(ImageFilter.GaussianBlur(radius=0.8))

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=haunted_negative,
        image=base.convert("RGB"),
        strength=0.18,
        guidance_scale=4.5,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=haunted_negative,
        image=detail,
        strength=0.22,
        guidance_scale=4.0,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _apply_spectral_grade(result, seed=seed)
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = result.filter(ImageFilter.GaussianBlur(0.12))
    result = ImageEnhance.Brightness(result).enhance(0.80)
    result = ImageEnhance.Color(result).enhance(0.75)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=96, threshold=2))
    return result

def _build_cave_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "cold water droplets falling from thin stalactites into dark shallow pools, faint ripples spreading across slick stone",
        "dense mineral teeth overhead with thick stalagmites rising below, subtle face-like stone patterns hidden in the distance",
        "low cave mist hugging the ground between wet rock formations, reflective puddles catching dim cold highlights",
        "fractured stone shelves and narrow side hollows receding into darkness, faint mineral sheen along damp walls",
        "ancient cave surfaces shaped by erosion and dripping calcite, unnatural geometry barely suggested within natural stone",
    ]
    return ", ".join([
        "standing inside an enormous uncanny cave chamber, first-person eye-level interior perspective, NOT aerial NOT top-down NOT exterior",
        "towering stalactites hanging above, stalagmites rising from the ground, wet uneven stone floor underfoot, deep darkness ahead",
        "very low light with cold reflective highlights on damp rock and shallow water only, darkness dominates",
        "no humans, no animals, no insects, no visible sky, no artificial structures — ancient subterranean emptiness",
        accents[rng.integers(0, len(accents))],
    ])

def _build_cave_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "ground-level first-person eye-level view through a vast uncanny cave with dripping ceilings and receding black depth",
        "immersive interior cavern perspective with towering mineral formations, reflective puddles, and heavy subterranean darkness",
        "deep natural cave passage framed by stalactites, slick rock walls, and a shadowed chamber opening ahead",
    ]
    props = [
        "wet stone surfaces reflecting faint cold highlights",
        "shallow puddles with slow water ripples across uneven rock",
        "layered stalagmites and fractured mineral shelves shaping the path forward",
        "low mist pooling near the cave floor between stone forms",
        "subtle face-like impressions hidden in converging shadow and rock texture",
    ]
    return ", ".join([
        anchors[rng.integers(0, len(anchors))],
        props[rng.integers(0, len(props))],
        props[rng.integers(0, len(props))],
        "desaturated mineral palette, dim cave reflections only, darkness dominates",
        "ground-level first-person cave interior camera only, NOT aerial NOT top-down, no creatures, no daylight, no manmade structures",
    ])


def _build_city_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "empty alley branching off between tall buildings, scattered debris and broken glass along the curb",
        "flickering distant sign glow reflecting weakly across weathered concrete and dark windows",
        "cracked asphalt with weeds pushing through seams, old papers drifting in weak wind",
        "rusted storefront framing and faded signage hanging unevenly above a silent sidewalk",
        "upper windows and shadowed facades creating barely face-like watching impressions",
    ]
    return ", ".join([
        "standing on a deserted abandoned city street, first-person eye-level urban perspective, NOT aerial NOT top-down NOT exterior skyline view",
        "tall buildings on both sides, cracked pavement underfoot, dark windows, empty alley or street corridor extending ahead",
        "very dim urban lighting, weak streetlight or distant failing illumination only, silence and shadow dominate",
        "no humans, no animals, no traffic, no active vehicles, no bright daylight — dead city atmosphere",
        accents[rng.integers(0, len(accents))],
    ])

def _build_city_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "ground-level first-person eye-level view along an abandoned city street with empty alley depth and silent building facades",
        "immersive street-level urban corridor inside a deserted city with broken windows, cracked pavement, and dead storefronts",
        "narrow alley opening into an empty street between tall weathered buildings, strong abandoned city depth",
    ]
    props = [
        "faded signage and rusted metal framing on old storefronts",
        "dark vacant windows with subtle observer-like shadow geometry",
        "broken glass, scattered debris, and dust gathered along sidewalks",
        "weak flickering distant light reflecting softly on dirty urban surfaces",
        "small weeds emerging through pavement cracks and curb seams",
    ]
    return ", ".join([
        anchors[rng.integers(0, len(anchors))],
        props[rng.integers(0, len(props))],
        props[rng.integers(0, len(props))],
        "desaturated concrete and rust palette, darkness and emptiness dominate",
        "ground-level first-person urban camera only, NOT aerial NOT top-down, no people, no active life, no vehicles in motion",
    ])


def _build_lab_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "failing fluorescent fixtures overhead casting weak cold light across dusty workstations and broken monitors",
        "glass containment chamber standing dark and reflective near old research benches and loose hanging cables",
        "warning labels, scattered papers, oxidized metal, and dead screens covering sterile surfaces",
        "sealed heavy door at the far end with worn hazard markings and faint monitor residue in the shadows",
        "cabinet reflections and glass edges forming almost human-like impressions in peripheral darkness",
    ]
    return ", ".join([
        "standing inside an abandoned underground laboratory, first-person eye-level interior perspective, NOT aerial NOT top-down NOT exterior",
        "workstations and equipment around the room, metal benches, dusty floor, corridor or sealed door receding ahead",
        "very low cold industrial lighting from failing fluorescents or dead monitor glow only, darkness dominates",
        "no humans, no animals, no insects, no active machines, no daylight — abandoned sterile research atmosphere",
        accents[rng.integers(0, len(accents))],
    ])

def _build_lab_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "ground-level first-person eye-level view inside a deserted research laboratory with dusty workstations and receding sterile depth",
        "immersive laboratory corridor perspective with containment glass, broken monitors, and sealed warning-marked doors",
        "abandoned lab interior with steel benches, hanging cables, and weak fluorescent flicker extending into darkness",
    ]
    props = [
        "dust-covered instruments and scattered papers on metal work surfaces",
        "glass containment chamber reflecting weak cold light and shadow",
        "oxidized equipment and disconnected cables hanging loosely from fixtures",
        "sealed heavy door with faded warning labels at the far end",
        "dark monitor screens and reflective cabinets creating subtle observer-like shapes",
    ]
    return ", ".join([
        anchors[rng.integers(0, len(anchors))],
        props[rng.integers(0, len(props))],
        props[rng.integers(0, len(props))],
        "sterile desaturated industrial palette, weak cold light only, silence dominates",
        "ground-level first-person lab interior camera only, NOT aerial NOT top-down, no living creatures, no outdoor view",
    ])


def _build_forest_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "dense tree trunks ahead with fog drifting softly between them and a narrow uneven path underfoot",
        "twisted roots crossing the forest floor, moss-covered bark, and dim canopy-filtered light",
        "branch patterns and bark fissures creating faint face-like impressions in peripheral vision",
        "leaf litter, damp soil, and subtle fungal traces near fallen wood in the shadows",
        "closely spaced trees with unnatural-feeling repetition fading into pale gray woodland haze",
    ]
    return ", ".join([
        "standing inside a dense uncanny forest, first-person eye-level natural perspective, NOT aerial NOT top-down NOT exterior landscape wide shot",
        "trees close on both sides, uneven ground underfoot, path or opening receding deeper ahead",
        "low natural filtered light through canopy, soft mist and shadow dominate",
        "no humans, no animals, no insects, no buildings, no artificial structures — isolated forest atmosphere",
        accents[rng.integers(0, len(accents))],
    ])

def _build_forest_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "ground-level first-person eye-level view through a dense uncanny forest with layered tree depth and drifting mist",
        "immersive woodland corridor perspective with closely spaced trunks, roots, and dim filtered canopy light",
        "deep forest interior path fading into fog between tall silent trees and tangled branches",
    ]
    props = [
        "moss-covered roots and leaf litter across the forest floor",
        "soft mist suspended between repeated tree trunks",
        "twisted bark textures and branches implying vague observer-like forms",
        "subtle fungal traces and damp earth in the lower shadows",
        "narrow path or natural opening disappearing into woodland haze",
    ]
    return ", ".join([
        anchors[rng.integers(0, len(anchors))],
        props[rng.integers(0, len(props))],
        props[rng.integers(0, len(props))],
        "desaturated woodland palette with filtered light only, softness and depth dominate",
        "ground-level first-person forest camera only, NOT aerial NOT top-down, no people, no creatures, no buildings",
    ])


def _build_underwater_detail_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    accents = [
        "broken shipwreck beams and corroded metal fragments resting across the seabed with drifting silt",
        "collapsed underwater structure fading into open black water beyond the wreckage",
        "tiny suspended particles filling the water column, weak cyan diffusion, and almost no visibility at distance",
        "a massive vague silhouette extremely far away in the darkness, implied more than seen",
        "sediment clouds slowly moving around ruined debris and encrusted surfaces on the ocean floor",
    ]
    return ", ".join([
        "standing or floating inside a deep underwater abyssal environment, first-person eye-level submerged perspective, NOT aerial NOT top-down NOT surface view",
        "wreckage or destroyed structure nearby, dark open water ahead, heavy depth and low visibility",
        "very dim cold underwater light with near-black distance, soft cyan diffusion only, darkness dominates",
        "no humans, no divers, no active vehicles, no bright coral reef, no surface sunlight — isolated deep ocean atmosphere",
        accents[rng.integers(0, len(accents))],
    ])

def _build_underwater_architecture_prompt(seed: str) -> str:
    rng = np.random.default_rng()
    anchors = [
        "first-person deep underwater view through wreckage and ruined structures into an open abyssal void",
        "immersive abyssal underwater perspective with corroded debris, drifting sediment, and dark ocean depth",
        "submerged ruin or shipwreck scene on the seafloor with broken beams and a distant black water horizonless expanse",
    ]
    props = [
        "suspended particulate matter and drifting silt clouding the water",
        "corroded metal surfaces and collapsed structural remains on the seabed",
        "faint weak bioluminescent residue or cold particulate glow in the dark water",
        "large ambiguous silhouette implied far beyond the visible wreckage",
        "sediment-covered debris and encrusted textures across broken surfaces",
    ]
    return ", ".join([
        anchors[rng.integers(0, len(anchors))],
        props[rng.integers(0, len(props))],
        props[rng.integers(0, len(props))],
        "dark abyssal palette, low visibility, heavy ocean depth dominates",
        "ground-level or first-person submerged camera only, NOT aerial NOT top-down, no divers, no bright reef life, no surface light",
    ])


def _diffuse_cave_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_cave_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_cave_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(
        f"{refiner_prompt}, "
        "first-person eye-level perspective inside a vast uncanny cave chamber, "
        "towering stalactites hanging above and thick stalagmites rising below, "
        "wet slick stone floor with shallow puddles and faint ripples, "
        "dark mineral walls receding into deep black cavern depth, "
        "cold reflective highlights on damp stone and calcite surfaces, "
        "subtle cave mist near the ground, natural geological realism, "
        "hidden face-like rock formations and ambiguous stone pareidolia, "
        "photorealistic, 8k detail, cinematic 35mm lens feel, global illumination, "
        "grounded natural cave textures, high dynamic range, sharp focus, "
        "no daylight, no artificial light, darkness dominates, immersive subterranean depth",
        max_words=CLIP_REFINER_WORDS, max_chars=140
    )
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(
        f"{detail_prompt}, "
        "first person view, detailed wet rock textures, calcite buildup, mineral sheen, "
        "fine cave wall erosion detail, shallow puddle reflections, dripping water, "
        "mist in recesses, realistic cave ceiling formations, subtle rock-face pareidolia",
        max_words=CLIP_DETAIL_WORDS, max_chars=170
    )

    cave_negative = _clip_prompt_safe((
        f"{negative_prompt}, "
        "aerial view, top-down view, birds-eye view, overhead perspective, drone shot, map view, "
        "exterior mountain view, outside cave entrance, open sky, daylight, sunlight beams, "
        "fantasy crystals, glowing gems, lava, magma, fire, magical cave, stylized rocks, "
        "sci-fi corridor, architecture, bricks, carved temple, built structure, "
        "humans, animals, insects, creatures, monsters, giant spiders, "
        "bright colors, neon palette, cartoon style, painterly rendering, "
        "clean flat walls, obvious tunnel symmetry, solid wall picture, third person view"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=cave_negative,
        image=image.convert("RGB"),
        strength=0.60,
        guidance_scale=6.2,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=cave_negative,
        image=base.convert("RGB"),
        strength=0.17,
        guidance_scale=5.6,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=cave_negative,
        image=detail,
        strength=0.21,
        guidance_scale=4.8,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _apply_spectral_grade(result, seed=seed)
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = result.filter(ImageFilter.GaussianBlur(0.10))
    result = ImageEnhance.Color(result).enhance(0.82)
    result = ImageEnhance.Contrast(result).enhance(1.20)
    result = ImageEnhance.Brightness(result).enhance(0.90)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=94, threshold=2))
    return result


def _diffuse_city_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_city_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_city_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(
        f"{refiner_prompt}, "
        "first-person eye-level perspective standing on an abandoned city street or alley, "
        "tall empty buildings rising on both sides, dark broken windows, cracked asphalt, "
        "rusted storefronts, debris along sidewalks, scattered broken glass, "
        "faded signs, weak distant streetlight or failing neon glow, "
        "urban silence and uncanny emptiness, subtle window pareidolia, "
        "photorealistic, 8k detail, cinematic 35mm lens feel, realistic materials, "
        "weathered concrete, dust, grime, oxidized metal, high dynamic range, sharp focus, "
        "no people, no traffic, no bright daylight, immersive abandoned city atmosphere",
        max_words=CLIP_REFINER_WORDS, max_chars=140
    )
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(
        f"{detail_prompt}, "
        "first person view, detailed cracked pavement, rust streaks, dirty windows, "
        "broken glass, weathered signage, curb debris, alley depth, "
        "subtle dust drift, realistic concrete and asphalt texture, faint silhouette-like window shadows",
        max_words=CLIP_DETAIL_WORDS, max_chars=170
    )

    city_negative = _clip_prompt_safe((
        f"{negative_prompt}, "
        "aerial view, top-down view, birds-eye view, overhead perspective, drone shot, skyline shot, city map view, "
        "busy traffic, cars moving, pedestrians, crowds, active storefronts, clean modern city, "
        "bright blue sky, sunny day, vivid cyberpunk neon, sci-fi metropolis, futuristic holograms, "
        "lush overgrown jungle city, dense plants, heavy vines everywhere, fantasy ruins, "
        "cartoon style, illustrated look, painterly rendering, toy city look, "
        "wide open plaza, giant monuments, third person view, exaggerated perspective"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=city_negative,
        image=image.convert("RGB"),
        strength=0.59,
        guidance_scale=6.4,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=city_negative,
        image=base.convert("RGB"),
        strength=0.16,
        guidance_scale=5.8,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=city_negative,
        image=detail,
        strength=0.20,
        guidance_scale=4.9,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _apply_spectral_grade(result, seed=seed)
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = result.filter(ImageFilter.GaussianBlur(0.08))
    result = ImageEnhance.Color(result).enhance(0.80)
    result = ImageEnhance.Contrast(result).enhance(1.26)
    result = ImageEnhance.Brightness(result).enhance(0.92)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=98, threshold=2))
    return result


def _diffuse_lab_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_lab_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_lab_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(
        f"{refiner_prompt}, "
        "first-person eye-level perspective inside an abandoned underground research laboratory, "
        "metal workstations, broken monitors, dusty surfaces, hanging cables, containment glass, "
        "sealed doors with warning labels, weak fluorescent flicker and faint dead monitor glow, "
        "cold sterile atmosphere overtaken by decay, oxidized equipment and scattered papers, "
        "subtle reflective pareidolia in glass and dark cabinets, "
        "photorealistic, 8k detail, cinematic 35mm lens feel, realistic steel, plastic, and glass materials, "
        "global illumination, high dynamic range, sharp focus, no daylight, no active machines, "
        "immersive eerie scientific realism, sterile silence dominates",
        max_words=CLIP_REFINER_WORDS, max_chars=140
    )
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(
        f"{detail_prompt}, "
        "first person view, detailed dust on equipment, oxidized metal, cracked screens, "
        "tarnished steel surfaces, reflective containment glass, warning label detail, "
        "loose cables, dead monitor glow, subtle contamination-like stains, realistic abandoned lab textures",
        max_words=CLIP_DETAIL_WORDS, max_chars=170
    )

    lab_negative = _clip_prompt_safe((
        f"{negative_prompt}, "
        "aerial view, top-down view, birds-eye view, overhead perspective, drone shot, "
        "bright hospital, clean modern lab, active scientists, robots, holograms, futuristic sci-fi corridor, "
        "outdoor view, windows with sunlight, daylight flooding in, warm cozy lighting, "
        "fantasy lab, glowing alien tech, magical effects, neon cyberpunk palette, "
        "cartoon style, painterly rendering, illustrated look, toy plastic look, "
        "humans, creatures, insects, obvious gore, third person view, exterior building view"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=lab_negative,
        image=image.convert("RGB"),
        strength=0.61,
        guidance_scale=6.1,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    base = base.filter(ImageFilter.GaussianBlur(radius=0.45))

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=lab_negative,
        image=base.convert("RGB"),
        strength=0.17,
        guidance_scale=5.3,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=lab_negative,
        image=detail,
        strength=0.22,
        guidance_scale=4.6,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _apply_spectral_grade(result, seed=seed)
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = result.filter(ImageFilter.GaussianBlur(0.10))
    result = ImageEnhance.Color(result).enhance(0.76)
    result = ImageEnhance.Contrast(result).enhance(1.22)
    result = ImageEnhance.Brightness(result).enhance(0.88)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=92, threshold=2))
    return result


def _diffuse_forest_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_forest_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_forest_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(
        f"{refiner_prompt}, "
        "first-person eye-level perspective inside a dense uncanny forest, "
        "closely spaced tall tree trunks on both sides, twisted roots across the ground, "
        "moss, damp bark, leaf litter, soft drifting fog between trees, "
        "dim filtered canopy light, narrow path or natural opening extending ahead, "
        "subtle bark pareidolia and distant silhouette-like tree groupings, "
        "photorealistic, 8k detail, cinematic 35mm lens feel, realistic bark and foliage materials, "
        "soft atmospheric depth, natural woodland realism, high dynamic range, sharp focus, "
        "no buildings, no creatures, no fantasy glow, immersive eerie forest calm",
        max_words=CLIP_REFINER_WORDS, max_chars=140
    )
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(
        f"{detail_prompt}, "
        "first person view, detailed bark texture, moss growth, root systems, "
        "forest floor leaf litter, damp earth, fine fog layering, subtle fungal traces, "
        "realistic branch structure, face-like bark impressions, deep woodland path detail",
        max_words=CLIP_DETAIL_WORDS, max_chars=170
    )

    forest_negative = _clip_prompt_safe((
        f"{negative_prompt}, "
        "aerial forest view, top-down view, birds-eye view, overhead perspective, drone shot, landscape panorama, "
        "bright fantasy forest, magical glow, fairy lights, enchanted woods, saturated neon greens, "
        "sunny meadow, open field, clear blue sky dominating, tropical jungle, huge flowers, "
        "animals, people, creatures, cabins, buildings, roads, modern objects, "
        "cartoon style, painterly rendering, stylized art, third person view, wall of trees blocking entire frame"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=forest_negative,
        image=image.convert("RGB"),
        strength=0.58,
        guidance_scale=6.0,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=forest_negative,
        image=base.convert("RGB"),
        strength=0.16,
        guidance_scale=5.4,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=forest_negative,
        image=detail,
        strength=0.20,
        guidance_scale=4.7,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = ImageEnhance.Color(result).enhance(0.86)
    result = ImageEnhance.Contrast(result).enhance(1.18)
    result = ImageEnhance.Brightness(result).enhance(0.94)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=94, threshold=2))
    return result


def _diffuse_underwater_pipeline(pipe, image: Image.Image, seed: str) -> Image.Image:
    prompt = _build_underwater_architecture_prompt(seed)
    refiner_prompt = BASE_REFINER_PROMPT
    negative_prompt = BASE_NEGATIVE_PROMPT
    detail_prompt = _build_underwater_detail_prompt(seed)

    prompt = _clip_prompt_safe(prompt, max_words=CLIP_MAIN_WORDS, max_chars=190)
    refiner_prompt = _clip_prompt_safe(
        f"{refiner_prompt}, "
        "first-person submerged perspective in a deep underwater abyss, "
        "broken shipwreck or ruined structure on the seafloor, corroded beams and debris, "
        "drifting silt clouds, suspended particles throughout the water, "
        "cold cyan-blue diffusion with near-black distance, open abyss beyond the wreckage, "
        "faint massive creature-like silhouette extremely far away and barely visible, "
        "photorealistic, 8k detail, cinematic underwater realism, realistic corrosion and sediment, "
        "volumetric depth haze, high dynamic range, sharp focus, "
        "no divers, no bright reef, no surface sunlight, oppressive deep ocean atmosphere",
        max_words=CLIP_REFINER_WORDS, max_chars=140
    )
    negative_prompt = _clip_prompt_safe(negative_prompt, max_words=CLIP_NEGATIVE_WORDS, max_chars=170)
    detail_prompt = _clip_prompt_safe(
        f"{detail_prompt}, "
        "first person view, detailed corroded metal, sediment buildup, suspended particulate matter, "
        "wreck textures, seabed debris, low visibility abyssal water, "
        "subtle bioluminescent residue, distant gigantic silhouette ambiguity, realistic underwater light falloff",
        max_words=CLIP_DETAIL_WORDS, max_chars=170
    )

    underwater_negative = _clip_prompt_safe((
        f"{negative_prompt}, "
        "aerial ocean view, top-down view, birds-eye view, overhead perspective, surface water view, beach view, "
        "bright tropical reef, colorful fish schools, coral paradise, sunbeams from surface, scuba divers, submarines, "
        "clear water, swimming pool look, fantasy sea monster close-up, explicit giant creature, "
        "cartoon style, painterly rendering, stylized art, neon aquatic colors, "
        "humans, boats above water, strong horizon line, third person view, clean modern underwater base"
    ), max_words=CLIP_NEGATIVE_WORDS, max_chars=170)

    base = pipe(
        prompt=prompt,
        negative_prompt=underwater_negative,
        image=image.convert("RGB"),
        strength=0.63,
        guidance_scale=5.8,
        num_inference_steps=DIFFUSE_DUNGEON_BASE_STEPS,
    ).images[0]

    base = base.filter(ImageFilter.GaussianBlur(radius=0.55))

    detail = pipe(
        prompt=detail_prompt,
        negative_prompt=underwater_negative,
        image=base.convert("RGB"),
        strength=0.18,
        guidance_scale=5.0,
        num_inference_steps=12,
    ).images[0]

    result = pipe(
        prompt=refiner_prompt,
        negative_prompt=underwater_negative,
        image=detail,
        strength=0.23,
        guidance_scale=4.4,
        num_inference_steps=DIFFUSE_DUNGEON_REFINER_STEPS,
    ).images[0]

    result = result.convert("RGB")
    result = _apply_spectral_grade(result, seed=seed)
    result = _solidify_color_fields(result)
    result = _lift_deep_blacks(result)
    result = _solidify_color_fields(result)
    result = result.filter(ImageFilter.SMOOTH_MORE)
    result = result.filter(ImageFilter.GaussianBlur(0.16))
    result = ImageEnhance.Color(result).enhance(0.72)
    result = ImageEnhance.Contrast(result).enhance(1.16)
    result = ImageEnhance.Brightness(result).enhance(0.84)
    result = result.filter(ImageFilter.UnsharpMask(radius=1.0, percent=88, threshold=2))
    return result


def diffuse_abstract(image: Image.Image, seed: str) -> Image.Image:
    pipe = get_pipe()
    pack_name = _current_pack_name().lower()
    if "cartoon" in pack_name:
        return _diffuse_cartoon_pipeline(pipe=pipe, image=image, seed=seed)
    if "dungeon" in pack_name or "castle" in pack_name:
        return _diffuse_dungeon_pipeline(pipe=pipe, image=image, seed=seed)
    if "sewer" in pack_name:
        return _diffuse_sewer_pipeline(pipe=pipe, image=image, seed=seed)
    if "hedge" in pack_name:
        return _diffuse_hedge_pipeline(pipe=pipe, image=image, seed=seed)
    if "haunted" in pack_name:
        return _diffuse_haunted_pipeline(pipe=pipe, image=image, seed=seed)
    if "cave" in pack_name:
        return _diffuse_cave_pipeline(pipe=pipe, image=image, seed=seed)
    if "city" in pack_name:
        return _diffuse_city_pipeline(pipe=pipe, image=image, seed=seed)
    if "lab" in pack_name:
        return _diffuse_lab_pipeline(pipe=pipe, image=image, seed=seed)
    if "forest" in pack_name:
        return _diffuse_forest_pipeline(pipe=pipe, image=image, seed=seed)
    if "underwater" in pack_name:
        return _diffuse_underwater_pipeline(pipe=pipe, image=image, seed=seed)
    else:
        return _diffuse_membrane_pipeline(pipe=pipe, image=image, seed=seed)


def generate_map_image(seed: str, use_diffusion: bool = True) -> Image.Image:
    active_pack = getattr(_prompt_pack_context, "pack", None)
    if active_pack is None:
        with _prompt_pack_scope():
            return generate_map_image(seed=seed, use_diffusion=use_diffusion)

    pack_name = _coerce_prompt_text(active_pack.get("name", "")).lower()
    is_cartoon_pack = "cartoon" in pack_name
    is_dungeon_pack = "dungeon" in pack_name or "castle" in pack_name
    base_image_size = WORK_IMAGE_SIZE
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
            radius=1.4 if is_cartoon_pack else (2.0 if is_dungeon_pack else 1.8),
            percent=140 if is_cartoon_pack else (190 if is_dungeon_pack else 180),
            threshold=2,
        )
    )
    canvas.paste(inner_resized, (border_size, border_size))
    # Keep emboss subtle so frosted glass remains smooth and cohesive.
    embossed = _apply_emboss_pop(canvas)
    emboss_alpha = 0.14 if is_cartoon_pack else (0.08 if is_dungeon_pack else 0.28)
    canvas = Image.blend(canvas, embossed, alpha=emboss_alpha)
    glass_intensity = 0.22 if is_cartoon_pack else (0.06 if is_dungeon_pack else 0.44)
    canvas = _apply_liquid_glass_finish(canvas, border_size=border_size, seed=seed, intensity=glass_intensity)
    # Final upscale only after border + post effects.
    canvas = _upscale_to_target(canvas, target_size=target_size)
    if is_dungeon_pack:
        canvas = canvas.filter(ImageFilter.UnsharpMask(radius=0.9, percent=82, threshold=2))
        canvas = ImageEnhance.Contrast(canvas).enhance(1.04)
    else:
        canvas = _final_clean_smooth(canvas)
    return canvas


def _generate_map_image_b64_now(seed: str, use_diffusion: bool = True) -> str:
    image = generate_map_image(seed=seed, use_diffusion=use_diffusion)
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return base64.b64encode(buffer.getvalue()).decode("ascii")


def _generate_map_image_payload_now(seed: str, use_diffusion: bool = True) -> dict:
    with _prompt_pack_scope() as active_pack:
        image = generate_map_image(seed=seed, use_diffusion=use_diffusion)
        buffer = io.BytesIO()
        image.save(buffer, format="PNG", optimize=True)
        return {
            "map_image": base64.b64encode(buffer.getvalue()).decode("ascii"),
            "theme": _coerce_prompt_text(active_pack.get("name", "")),
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
