import copy
import random
from typing import Dict, List, Optional, Union


BASE_PACK: Dict[str, object] = {
    "name": "default_membrane_cave",
    "PROMPT_COMPOSITION_ELITE": [
        "distributed liquid plasma currents with no dominant center",
        "prismatic flow fields spreading energy across the full frame",
        "glassy continuous motion with balanced salience and no focal anchor",
    ],
    "PROMPT_COLORS_ELITE": [
        "iridescent full-spectrum plasma glowing through translucent glass-like gradients",
        "vivid spectral colors diffusing through soft violet and twilight contrast",
        "molten rainbow hues refracting like cosmic stained glass in light",
        "nebula blues pinks and golds illuminated by radiant plasma highlights",
        "ultra-vibrant psychedelic chroma with electric hue transitions",
        "high-saturation neon spectrum with trippy prismatic color drift",
    ],
    "SCIENCE_CUES_ELITE": [
        "forms shimmering between clarity and color-rich dissolution",
        "fractal plasma filaments branching through luminous transparent layers",
        "drifting salience glowing like suspended particles in radiant fluid light",
        "depth that fades into colorful mist with soft shadow contrast",
    ],
    "PROMPT_PAREIDOLIA_ELITE": [
        "soft facial symmetry flickering within iridescent plasma glow",
        "a gentle sense of presence emerging from luminous flowing color",
        "watchful impressions forming within refracted rainbow currents",
    ],
    "PROMPT_NEUROCHEM_ELITE": [
        "slow revelations appearing like shapes forming in glowing liquid color",
        "glassy near-symmetry encouraging deep immersive scanning",
        "smooth plasma motion that feels warm colorful and trance-inducing",
        "a radiant emotional ambiguity that lingers like chromatic afterlight",
    ],
    "PROMPT_TEMPORAL_ELITE": [
        "colorful plasma ripples unfolding like waves within waves of light",
        "fine prismatic detail suspended within slow liquid motion",
        "fractal glow patterns revealing more color the longer you gaze",
    ],
    "PROMPT_EDGE_ELITE": [
        "forms that feel meaningful yet gently out of reach",
        "imagery like a memory seen through luminous prismatic glass",
        "suggestions of hidden meaning glowing beneath vibrant surfaces",
    ],
    "PROMPT_ARCHETYPES_ELITE": [
        "radiant spirals suggesting joyful cosmic transformation",
        "soft glowing light blooming from colorful translucent space",
        "circular energy flows dissolving into endless luminous drift",
    ],
    "PROMPT_SCALE_ELITE": [
        "a scale that feels like both a glowing cell and a radiant galaxy",
        "color-rich plasma structures drifting between micro and cosmic vastness",
        "tiny prismatic details floating inside infinite luminous depth",
    ],
    "PROMPT_FLOW_RULES": [
        "prioritize vibrant luminosity with darkness used only for contrast",
        "favor iridescent transparency over heavy shadow",
        "let colors refract and blend like liquid light",
        "keep motion slow smooth and hypnotically radiant",
        "maintain a bright trance-like glow balanced by subtle depth contrast",
        "avoid single-point composition and keep attention distributed",
        "enhance liquid continuity so forms melt and flow like luminous fluid",
    ],
    "PROMPT_COMPOSITION_ELITE_DEFINED": [
        "a high-energy plasma vortex with laminar flow wrapping a luminous central core",
        "an asymmetrical prismatic current bending like anisotropic liquid crystal in slow orbital motion",
        "a contained glass-like flow field with inward-looping luminous circulation",
    ],
    "PROMPT_COLORS_ELITE_DEFINED": [
        "iridescent full-spectrum plasma rendered through translucent glasslike gradients",
        "broad spectral dispersion with violet-biased shadow contrast",
        "high-saturation rainbow refraction resembling cosmic stained glass optics",
        "nebula-class blues magentas and golds with localized plasma bloom highlights",
    ],
    "SCIENCE_CUES_ELITE_DEFINED": [
        "phase transitions between sharp transparency and chromatic diffusion",
        "fractal plasma filaments branching across semi-transparent volumetric layers",
        "particle-like salience suspended in radiant fluid illumination",
        "volumetric depth falloff into chromatic haze with soft penumbral shadowing",
    ],
    "PROMPT_PAREIDOLIA_ELITE_DEFINED": [
        "low-amplitude bilateral symmetry embedded within plasma luminosity",
        "emergent face-like topology formed by refractive flow convergence",
        "observer-like impressions arising from curved spectral interference patterns",
    ],
    "PROMPT_NEUROCHEM_ELITE_DEFINED": [
        "slow-forming gestalt structures emerging from chromatic fluid dynamics",
        "near-symmetry fields encouraging prolonged visual scanning",
        "smooth low-frequency plasma motion with warm spectral bias",
        "affective ambiguity generated through persistent chromatic afterglow",
    ],
    "PROMPT_TEMPORAL_ELITE_DEFINED": [
        "nested plasma wave interference evolving across long temporal scales",
        "micro-prismatic detail preserved under slow fluid advection",
        "iterative fractal luminance patterns increasing with dwell time",
    ],
    "PROMPT_EDGE_ELITE_DEFINED": [
        "semantic ambiguity near recognition threshold",
        "memory-like imagery filtered through refractive prismatic distortion",
        "latent structure implied beneath high-luminance chromatic surfaces",
    ],
    "PROMPT_ARCHETYPES_ELITE_DEFINED": [
        "radiant logarithmic spirals implying energetic transformation",
        "diffuse photonic bloom expanding through translucent chromatic space",
        "closed-loop energy flows dissolving into persistent luminous drift",
    ],
    "PROMPT_SCALE_ELITE_DEFINED": [
        "scale ambiguity spanning cellular to galactic regimes",
        "multi-scale plasma structures bridging microfluidic and cosmological cues",
        "sub-pixel prismatic detail embedded within deep volumetric infinity",
    ],
    "PROMPT_FLOW_RULES_DEFINED": [
        "prioritize high luminance and spectral richness using darkness only for contrast scaffolding",
        "bias toward refractive transparency and volumetric glow over occlusive shadow",
        "favor smooth spectral blending and dispersion-based color mixing",
        "maintain low-frequency continuous motion without abrupt transitions",
        "sustain a stable trance-inducing luminance envelope with controlled depth gradients",
    ],
    "NEURAL_CAVE_SUBJECT": [
        "immersive biomorphic membrane cave with flowing tunnel geometry",
        "mysterious organic cavern built from smooth cellular lattice pathways",
        "deep exploratory tissue-like cavern formed by connected luminous membranes",
    ],
    "NEURAL_CAVE_STRUCTURE": [
        "silky membrane lattice with thick branching strands and rounded cell windows",
        "semi-translucent webbed tunnels with smooth curvature and clean connective filaments",
        "organic net-like structures with hollow pockets layered into arching cave forms",
    ],
    "NEURAL_CAVE_GLOW": [
        "soft inner glow with pale highlights against deep shadow pockets",
        "gentle luminous diffusion through translucent membranes and tunnel voids",
        "subtle pearl-like lighting with lavender and warm ivory accents",
    ],
    "NEURAL_CAVE_PAREIDOLIA": [
        "hidden face-like impressions emerging only through prolonged viewing",
        "subconscious watchful presence implied by refractive symmetry",
        "near-facial topology hinted in the flow without explicit faces",
    ],
    "NEURAL_CAVE_MOOD": [
        "introspective, calming, mesmerizing, and mysterious atmosphere",
        "liminal, contemplative, and emotionally resonant visual tone",
        "captivating depth designed for pattern recognition and sustained attention",
    ],
    "NEURAL_CAVE_SCIENCE": [
        "engineered neuroaesthetic balance of complexity and harmony",
        "fractal-like smooth branching and fascia-like translucency for slow scanning",
        "progressive revelation with meaningful ambiguity and strong visual retention",
    ],
    "NEURAL_CAVE_MORPH": [
        "alveoli-like chambers linked by elastic membrane strands at varied scales",
        "dense organic web networks opening into larger tunnel cavities",
        "fascia-like lattice sheets stretched across deep hollow spaces",
    ],
    "NEURAL_CAVE_DEPTH": [
        "first-person cave depth with a clear central passage and receding membrane chambers",
        "immersive tunnel perspective with layered web arches and nested hollows",
        "deep volumetric cavern with foreground netting and distant open voids",
    ],
    "NEURAL_CAVE_ORGANIC_PRESENCE": [
        "subtle organic life hints, faint plants and soft fungal growth in recesses",
        "occasional tiny spider-like silhouettes and delicate biological traces in shadows",
        "rare human-like face suggestions emerging softly from membrane patterns",
        "light signs of living matter integrated naturally into tunnel walls",
    ],
    "CORE_PROMPTS": [
        "first-person biomorphic membrane cave tunnel, deep open passage",
        "layered hollow chambers and arches, strong depth perspective, not flat",
    ],
    "ORGANIC_SHORT": [
        "subtle organic traces in recesses, non-dominant",
        "occasional human silhouette hinted in distant membrane shadows",
        "rare spider-like form along webbed strands, understated",
        "soft face-like hints blended into tunnel walls",
    ],
    "BASE_REFINER_PROMPT": (
        "enhance smooth biomorphic membrane linework, refine lattice web continuity, "
        "ultra-clean gradients, sharpen cellular contours and subtle pareidolia"
    ),
    "BASE_NEGATIVE_PROMPT": (
        "building, buildings, architecture, city, temple, sci-fi corridor, "
        "room interior, floor tiles, hallway, rough rocks, jagged edges, geometric grids, "
        "mechanical structures, wires, cables, grainy, noisy, low detail, clutter, "
        "distortion, oversharpen, dense plants, dominant flowers, giant mushrooms, insect swarm, crowd, gore, "
        "text, watermark, logo, flat pattern, wallpaper, tessellation, repeating net, mosaic, mandala, top-down view"
    ),
}


def _pack_with_overrides(name: str, overrides: Dict[str, object]) -> Dict[str, object]:
    pack = copy.deepcopy(BASE_PACK)
    pack["name"] = name
    pack.update(overrides)
    return pack


PROMPT_PACKS: List[Dict[str, object]] = [
    BASE_PACK,
    _pack_with_overrides(
        "castle_dungeon_tunnel",
        {
            "PROMPT_COMPOSITION_ELITE": [
                "descending vaulted corridor with chained arches pulling into deep shadow",
                "off-center dungeon passage with layered alcoves and receding vault ribs",
                "catacomb tunnel composition with carved masonry ribs and a distant lit chamber",
            ],
            "PROMPT_COLORS_ELITE": [
                "torchlit amber and desaturated crimson highlights in deep blue-black tunnel shadows",
                "ancient stone palette with subdued gold accents and restrained violet undertones",
                "warm firelight bleeding into cold subterranean darkness with low saturation",
                "muted obsidian and weathered burgundy base with occasional warm highlights",
            ],
            "SCIENCE_CUES_ELITE": [
                "high-frequency stone texture against soft volumetric haze for depth tension",
                "contrast islands around torch points with unresolved shadow geometry",
                "repeating gothic rhythm anchored by masonry blocks and buttressed wall mass",
                "clear near-field detail with uncertain far-field vanishing space",
            ],
            "PROMPT_PAREIDOLIA_ELITE": [
                "faint face-like shadow topology in worn vault joints",
                "watchful impressions implied by arch symmetry and void placement",
                "subtle sentient presence suggested by torchlit negative space",
            ],
            "PROMPT_NEUROCHEM_ELITE": [
                "progressive discovery through layered arches and hidden side chambers",
                "near-symmetry in vault spacing that never fully resolves",
                "stone relief and carved joints that hold scanning attention",
            ],
            "PROMPT_TEMPORAL_ELITE": [
                "recursive arch motifs that reveal secondary patterns over time",
                "micro detail in mortar cracks, damp stone, and iron fixtures",
                "slow visual loop from foreground masonry to distant corridor glow",
            ],
            "PROMPT_EDGE_ELITE": [
                "almost-symbolic relic forms embedded in dungeon walls",
                "memory-like impressions of forgotten passages",
                "ambiguous markings that feel meaningful but unresolved",
            ],
            "PROMPT_ARCHETYPES_ELITE": [
                "threshold-like archways suggesting descent and transformation",
                "light-from-darkness motifs at corridor endpoints",
                "spiral-like erosion and rib patterns implying time and depth",
            ],
            "PROMPT_SCALE_ELITE": [
                "ambiguous scale between intimate corridor and monumental undercroft",
                "small carved niches nested inside fortress-scale architecture",
                "micro fissure detail embedded in vast subterranean space",
            ],
            "PROMPT_FLOW_RULES": [
                "preserve strong tunnel perspective and vaulted rhythm",
                "keep dark mass dominant with selective warm highlights",
                "emphasize stone wall geometry and structural depth over organic web motifs",
                "lighting must come only from torches, candles, and lanterns with warm falloff and deep shadows",
                "show dungeon props frequently: doors, skeletons, chains, spider webs, stone reliefs",
                "avoid broad pastel wash or neon colors; prioritize moody cinematic contrast",
            ],
            "PROMPT_COMPOSITION_ELITE_DEFINED": [
                "gothic vault sequence with staggered ribs and lateral alcoves",
                "catacomb axis with offset focal depth and crossing web arcs",
                "fortress underpass with layered arch compression toward a lit exit",
            ],
            "PROMPT_COLORS_ELITE_DEFINED": [
                "ember-orange and candle-gold accents over deep mineral blacks",
                "burgundy-violet shadow fields with selective warm reflections",
                "cold blue subsurface tones beneath torchlit upper planes",
            ],
            "SCIENCE_CUES_ELITE_DEFINED": [
                "multi-scale texture from rough stone grain to fine chisel and mortar detail",
                "luminance cliffs around torch points to direct eye traversal",
                "gestalt tension between rigid masonry rhythm and broken ruin irregularities",
            ],
            "PROMPT_PAREIDOLIA_ELITE_DEFINED": [
                "low-amplitude face geometry in stain and crack patterns",
                "observer-like topology suggested by paired arch voids",
                "quiet anthropomorphic cues without explicit characters",
            ],
            "PROMPT_NEUROCHEM_ELITE_DEFINED": [
                "layered revelation from foreground webs to far chamber silhouettes",
                "repeating forms with slight asymmetry for sustained scan loops",
                "controlled unease balanced by coherent path readability",
            ],
            "PROMPT_TEMPORAL_ELITE_DEFINED": [
                "nested vault cadence producing temporal depth in view progression",
                "microdetails that emerge along beam falloff and stone seams",
                "recurrent corridor motifs that reward revisiting",
            ],
            "PROMPT_EDGE_ELITE_DEFINED": [
                "latent icon-like carvings near recognition threshold",
                "half-legible historical traces embedded in masonry",
                "symbolic ambiguity in old ritual geometry",
            ],
            "PROMPT_ARCHETYPES_ELITE_DEFINED": [
                "descent corridor archetype with distant revelation light",
                "gate-like forms denoting passage and transition",
                "chamber circles and rings hinting at ritual continuity",
            ],
            "PROMPT_SCALE_ELITE_DEFINED": [
                "human-scale tunnel cues against cathedral-scale vault spans",
                "tiny web lattices suspended in monumental stone volume",
                "intimate debris detail contrasted with abyssal depth",
            ],
            "PROMPT_FLOW_RULES_DEFINED": [
                "maintain legible corridor pull from foreground to deep background",
                "favor directional lighting gradients over flat illumination",
                "keep web structures supportive of architecture, not replacing it",
            ],
            "NEURAL_CAVE_SUBJECT": [
                "first-person descent through a castle dungeon tunnel with heavy vaulted masonry",
                "ancient subterranean passage beneath fortress ruins with carved stone arches",
                "catacomb tunnel beneath a castle with massive blocks and worn structural ribs",
            ],
            "NEURAL_CAVE_STRUCTURE": [
                "heavy stone vaults with carved ribs crossing gothic tunnel chambers",
                "arched dungeon corridors with buttressed walls and recessed alcoves",
                "rough-hewn catacomb geometry built from stacked masonry and worn stone joints",
            ],
            "NEURAL_CAVE_GLOW": [
                "low torch and candle glow with ember highlights against deep shadowed hollows",
                "iron lantern pools of warm light fading quickly into darkness",
                "subtle warm torch-candle transitions across layered dungeon arches",
            ],
            "NEURAL_CAVE_MORPH": [
                "interlaced vault ribs and masonry spans opening into deeper tunnel shafts",
                "stone chambers with branching side passages and carved lintels",
                "nested dungeon hollows connected by heavy arching supports",
            ],
            "NEURAL_CAVE_DEPTH": [
                "strong central tunnel pull with receding arches and distant chamber light",
                "descending dungeon perspective with layered vault depth and side alcoves",
                "long subterranean passage depth with foreground stone detail and far voids",
            ],
            "NEURAL_CAVE_PAREIDOLIA": [
                "face-like shadows implied by broken torchlight and arch joints",
                "watchful hints emerging from symmetric vault pockets",
                "subtle presence encoded in stone-web intersections",
            ],
            "NEURAL_CAVE_MOOD": [
                "ominous, immersive, ancient, and contemplative",
                "slow-burning tension with mystical subterranean calm",
                "dark medieval atmosphere with selective wonder",
            ],
            "NEURAL_CAVE_SCIENCE": [
                "high-contrast focal stepping stones for fast visual capture",
                "near-symmetry in structure with unresolved organic interruptions",
                "depth cues engineered for prolonged corridor scanning behavior",
            ],
            "NEURAL_CAVE_ORGANIC_PRESENCE": [
                "frequent spider webs and occasional spiders in dark vaulted corners",
                "rare distant human figure reading as explorer or sentinel",
                "light fungal traces on damp stone seams, never dominant",
                "frequent skeleton remains near wall alcoves or passage turns",
                "regular torch-bearing doorways and heavy wooden dungeon doors in side recesses",
            ],
            "CORE_PROMPTS": [
                "first-person castle dungeon tunnel with deep receding passage",
                "layered stone arches and buttressed walls, strong depth perspective",
            ],
            "ORGANIC_SHORT": [
                "spider webs near damp corner stones",
                "rare distant human figure silhouette in torch haze",
                "subtle organic growths in wall recesses",
                "faint face-like shadows in old stone webbing",
                "skeleton remains near doorway shadows",
                "heavy iron-banded wooden doors lit by torch sconces",
            ],
            "BASE_REFINER_PROMPT": (
                "enhance realistic vaulted corridor depth, refine worn stone linework, "
                "clean torchlight gradients, sharpen subtle pareidolia in shadows, "
                "preserve ominous low-saturation medieval dungeon mood with clean realistic surfaces and torch-candle-lantern lighting only"
            ),
            "BASE_NEGATIVE_PROMPT": (
                "modern architecture, bright city interiors, sci-fi metal corridors, "
                "flat wallpaper pattern, toy plastic look, low detail clutter, text watermark logo, "
                "cartoon bubble style dominance, insect swarm, crowd, gore, neon glow, oversaturated fantasy colors, "
                "sunlight, skylight, moonlight, window light, outdoor light, open sky"
            ),
        },
    ),
    _pack_with_overrides(
        "cartoon_whimsical_tunnel",
        {
            "PROMPT_COMPOSITION_ELITE": [
                "1930s rubber hose toy tunnel corridor with one clear hero in foreground and clean support cast depth",
                "cuphead style tunnel passage lined with toy shelves and perspective-consistent character scaling",
                "vintage cel-animated toy-box tunnel interior with clear foreground-midground-background staging and open breathing space",
            ],
            "PROMPT_COLORS_ELITE": [
                "fully colored characters using one dominant hue each with cohesive teal-cyan environment harmony",
                "high-contrast vintage cartoon palette with flat fills and limited cel shadow steps",
                "clean primaries and candy accents with strong black ink outlines and white glove highlights",
                "bold warm-cool contrast with palette consistency and no random hue breaks",
            ],
            "SCIENCE_CUES_ELITE": [
                "high-contrast silhouette readability with stable focal hierarchy and clear visual subject",
                "perspective-coherent scale progression for characters and props along tunnel depth",
                "character clarity prioritized over texture noise and procedural micro-detail",
                "clean shape language with consistent design motifs across all figures",
            ],
            "PROMPT_PAREIDOLIA_ELITE": [
                "expressive oversized eyes with clean pie-cut pupil logic",
                "friendly but coherent cartoon faces with stable eye-mouth placement",
                "character silhouettes that read instantly with unified species language",
            ],
            "PROMPT_NEUROCHEM_ELITE": [
                "progressive reward through layered toy details and hidden mini-characters",
                "dense visual novelty inside a coherent toy-shop environment",
                "playful near-symmetry broken by small toy-character surprises",
            ],
            "PROMPT_TEMPORAL_ELITE": [
                "nested doodle patterns revealing extra jokes and symbols over time",
                "micro icon textures embedded in bold cartoon masses",
                "repeating pop motifs with delayed recognition layers",
            ],
            "PROMPT_EDGE_ELITE": [
                "almost-symbolic graffiti doodles suggesting hidden narrative",
                "nostalgic cartoon cues that feel familiar but unresolved",
                "playful emblem-like marks near recognition threshold",
            ],
            "PROMPT_ARCHETYPES_ELITE": [
                "portal-like toy archways suggesting playful level transitions",
                "spiral pop motifs implying movement and playful transformation",
                "hero-path depth cues with bright reward-like highlights",
            ],
            "PROMPT_SCALE_ELITE": [
                "tiny mascot characters nested among oversized toy props and shelves",
                "toy-scale doodles embedded in a giant playful room interior",
                "micro icon clusters floating within macro toy-box depth",
            ],
            "PROMPT_FLOW_RULES": [
                "prioritize character readability and coherent anatomy over background texture complexity",
                "keep thick confident black outlines with near-uniform contour weight",
                "use flat cel color blocks with one simple shadow tone and subtle edge light",
                "render living subjects in colors that clearly contrast with environment by hue and value",
                "keep backgrounds slightly less saturated so characters and organic life pop immediately",
                "ground characters with cast shadows and contact points on floor surfaces",
                "avoid melted faces, floating limbs, and random scribble line artifacts",
                "environment should read as toy-like tunnel architecture, not sewer or grimy industrial space",
                "preserve open negative space around main characters for focal clarity",
            ],
            "PROMPT_COMPOSITION_ELITE_DEFINED": [
                "rubber hose toy-tunnel aisle with one hero anchor and two support characters scaled by depth",
                "vintage cartoon toy-lined tunnel corridor with readable shelf lines and playful props guiding perspective",
                "cleanly staged animated toy-box tunnel passage with uncluttered focal lane and secondary side action",
            ],
            "PROMPT_COLORS_ELITE_DEFINED": [
                "single dominant color per character with complementary accents and stable palette grouping",
                "flat cel fills, minimal gradients, strong black contour contrast, bright controlled highlights",
                "clean saturated hues with no accidental purple or off-palette color drift",
            ],
            "SCIENCE_CUES_ELITE_DEFINED": [
                "edge-first readability with dense icon salience islands",
                "gestalt grouping from repeated cartoon emblems and faceshapes",
                "distributed attention via color pops, shape rhythm, and layered overlap",
            ],
            "PROMPT_PAREIDOLIA_ELITE_DEFINED": [
                "character-like hints emerging from overlapping doodle clusters",
                "implied gaze in paired sticker icons and bubble motifs",
                "friendly face topology hidden in graffiti-like wall textures",
            ],
            "PROMPT_NEUROCHEM_ELITE_DEFINED": [
                "rapid first-glance hook with deep hidden symbol discovery",
                "constant novelty pulses from layered cartoon micro-events",
                "coherent path flow inside intentional cartoon scene density",
            ],
            "PROMPT_TEMPORAL_ELITE_DEFINED": [
                "recursive doodle filigree that reveals sub-scenes over time",
                "foreground crowding with cleaner distant passage readability",
                "motif recurrence that deepens after repeated viewing",
            ],
            "PROMPT_EDGE_ELITE_DEFINED": [
                "symbol-like pop emblems implying story without explicit text",
                "near-recognition cartoon relics inviting interpretation",
                "nostalgic icon fragments embedded in tunnel decor",
            ],
            "PROMPT_ARCHETYPES_ELITE_DEFINED": [
                "arcade-like portal archetypes and playful spiral motifs",
                "reward-light cues from depth signaling progression",
                "hero-route composition hidden inside cartoon noise",
            ],
            "PROMPT_SCALE_ELITE_DEFINED": [
                "mini character ecosystems inside giant pop-art tunnel chambers",
                "toy-scale icon logic against exaggerated cavern depth",
                "dense micro ornament over broad cartoon masses",
            ],
            "PROMPT_FLOW_RULES_DEFINED": [
                "favor bold contour anchors and clean vector-like fills over noisy texture grain",
                "keep tunnel readability clear while preserving simple staging around character groups",
                "maintain deliberate subject-background contrast for instant readability of life forms",
                "maintain cuphead-like cel shading, coherent expressions, and stable limb proportions",
            ],
            "NEURAL_CAVE_SUBJECT": [
                "1930s rubber hose cartoon toy tunnel adventure with coherent mascot cast",
                "cuphead-inspired toy-lined tunnel scene with clean character posing and readable toy props",
                "vintage 2D animation toy-box tunnel interior with unified species design and stable perspective",
            ],
            "NEURAL_CAVE_STRUCTURE": [
                "thick ink contours on rounded toy shelves, boxes, and playful tunnel props with consistent line weight",
                "flat cel-shaded forms with clear tunnel floor perspective and strong readability",
                "curved cartoon set architecture with stable vanishing pull and no grime-heavy industrial cues",
            ],
            "NEURAL_CAVE_GLOW": [
                "bright animation-style highlights with soft bloom accents",
                "clean spotlighting and color-block shadows like a cartoon frame",
                "luminous but readable lighting with minimal noise texture",
            ],
            "NEURAL_CAVE_MOOD": [
                "energetic vintage cartoon mood with playful but coherent visual storytelling",
                "nostalgic animation charm with clear character focus and stable scene rhythm",
                "joyful retro tunnel adventure with readable action and strong depth pull",
            ],
            "NEURAL_CAVE_MORPH": [
                "rounded toy-tunnel zones with repeating motifs that support character action staging",
                "playful toy props and signage arranged to reinforce perspective and scale consistency",
                "layered shelves and arches guiding the eye while preserving foreground character clarity",
            ],
            "NEURAL_CAVE_DEPTH": [
                "clear toy-tunnel depth with layered shelves and grounded character placement",
                "storybook perspective pull through a playful toy tunnel with controlled density and visible negative space",
                "deep toon toy-box tunnel interior where characters scale naturally with distance and floor contact",
            ],
            "NEURAL_CAVE_PAREIDOLIA": [
                "friendly face-like hints hidden in doodle piles",
                "watchful cartoon eyespots implied in paired symbols",
                "character impressions emerging from overlapping outline clusters",
            ],
            "NEURAL_CAVE_SCIENCE": [
                "high-contrast icon readability for instant capture and retention",
                "dense novelty field balanced with clear directional depth",
                "repetition-variation rhythm engineered for repeated scanning without abstraction loss",
            ],
            "NEURAL_CAVE_ORGANIC_PRESENCE": [
                "occasional playful full cartoon characters with coherent body language",
                "rare stylized spider motif as a secondary background detail",
                "light cartoon life cues that support the scene without cluttering the subject",
            ],
            "CORE_PROMPTS": [
                "first-person 1930s rubber hose cartoon toy tunnel with coherent mascot characters",
                "flat cel shading, thick black outlines, grounded figures, readable playful tunnel depth",
            ],
            "ORGANIC_SHORT": [
                "occasional secondary cartoon character scaled correctly in distance",
                "rare cute stylized spider mascot near tunnel edge with clean silhouette",
                "subtle mascot face symbols in props with coherent expression",
                "light whimsical life hints that remain secondary to the hero character",
            ],
            "BASE_NEGATIVE_PROMPT": (
                "psychedelic fractal tunnel, procedural geometric maze, abstract-only forms, photoreal rendering, "
                "mismatched art styles, inconsistent character sizes, white unshaded characters, uncolored characters, "
                "floating figures, clipping geometry, broken anatomy, blob figures, melted faces, malformed eyes, "
                "thin outlines, no outlines, painterly gradients, noisy dither texture, random palette breaks, "
                "horror gore, top-down map view, plain empty composition, sewer, grime, sludge, dirty pipes, rusted industrial tunnel"
            ),
            "BASE_REFINER_PROMPT": (
                "enhance thick uniform black outlines, clean flat cel color blocks, coherent rubber hose anatomy, "
                "refine expressive faces and grounded cast shadows, preserve perspective-consistent character scale, "
                "keep environment playful toy-tunnel clean and hand-drawn"
            ),
        },
    ),
]


# Key schema must stay aligned with imagegen.py variable names.
PROMPT_PACK_KEYS = [
    "PROMPT_COMPOSITION_ELITE",
    "PROMPT_COLORS_ELITE",
    "SCIENCE_CUES_ELITE",
    "PROMPT_PAREIDOLIA_ELITE",
    "PROMPT_NEUROCHEM_ELITE",
    "PROMPT_TEMPORAL_ELITE",
    "PROMPT_EDGE_ELITE",
    "PROMPT_ARCHETYPES_ELITE",
    "PROMPT_SCALE_ELITE",
    "PROMPT_FLOW_RULES",
    "PROMPT_COMPOSITION_ELITE_DEFINED",
    "PROMPT_COLORS_ELITE_DEFINED",
    "SCIENCE_CUES_ELITE_DEFINED",
    "PROMPT_PAREIDOLIA_ELITE_DEFINED",
    "PROMPT_NEUROCHEM_ELITE_DEFINED",
    "PROMPT_TEMPORAL_ELITE_DEFINED",
    "PROMPT_EDGE_ELITE_DEFINED",
    "PROMPT_ARCHETYPES_ELITE_DEFINED",
    "PROMPT_SCALE_ELITE_DEFINED",
    "PROMPT_FLOW_RULES_DEFINED",
    "NEURAL_CAVE_SUBJECT",
    "NEURAL_CAVE_STRUCTURE",
    "NEURAL_CAVE_GLOW",
    "NEURAL_CAVE_PAREIDOLIA",
    "NEURAL_CAVE_MOOD",
    "NEURAL_CAVE_SCIENCE",
    "NEURAL_CAVE_MORPH",
    "NEURAL_CAVE_DEPTH",
    "NEURAL_CAVE_ORGANIC_PRESENCE",
    "CORE_PROMPTS",
    "ORGANIC_SHORT",
    "BASE_REFINER_PROMPT",
    "BASE_NEGATIVE_PROMPT",
]


def _validate_prompt_pack_schema() -> None:
    for idx, pack in enumerate(PROMPT_PACKS):
        missing = [k for k in PROMPT_PACK_KEYS if k not in pack]
        if missing:
            raise KeyError(f"Prompt pack '{pack.get('name', idx)}' missing keys: {missing}")


# Easy manual selection:
# - Set ACTIVE_PROMPT_PACK to a pack name (recommended), e.g. "default_membrane_cave"
# - Set ACTIVE_PROMPT_PACK to "random" for random pack each generation
# - You may also set ACTIVE_PROMPT_PACK to an integer index
ACTIVE_PROMPT_PACK: Union[str, int] = "castle_dungeon_tunnel"

# Backward-compatible index selector (used only when ACTIVE_PROMPT_PACK is None).
ACTIVE_PROMPT_PACK_INDEX: Optional[int] = None

PACK_NAMES = [str(p.get("name", f"pack_{i}")) for i, p in enumerate(PROMPT_PACKS)]


def get_prompt_pack(index: Optional[int] = None) -> Dict[str, object]:
    if index is not None:
        idx = index
    else:
        selector: Union[str, int, None] = ACTIVE_PROMPT_PACK
        if selector is None:
            selector = ACTIVE_PROMPT_PACK_INDEX

        if selector is None or selector == "random":
            idx = random.randrange(len(PROMPT_PACKS))
        elif isinstance(selector, int):
            idx = selector
        elif isinstance(selector, str):
            try:
                idx = PACK_NAMES.index(selector)
            except ValueError as exc:
                raise KeyError(f"Unknown prompt pack name: {selector}. Valid names: {PACK_NAMES}") from exc
        else:
            raise TypeError(f"Invalid ACTIVE_PROMPT_PACK selector type: {type(selector)}")

    if idx < 0 or idx >= len(PROMPT_PACKS):
        raise IndexError(f"Prompt pack index out of range: {idx}")
    return PROMPT_PACKS[idx]


_validate_prompt_pack_schema()
