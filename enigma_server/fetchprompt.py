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
   _pack_with_overrides(
        "sewer_tunnel",
        {
            "PROMPT_COMPOSITION_ELITE": [
                "long brick sewer tunnel receding deep into near-total darkness with a single strong vanishing point and low arched ceiling",
                "first-person sewer corridor with garbage-scattered floor, pipe-lined walls, and stagnant puddles reflecting dim ambient light",
                "extensive brick tunnel interior with clear foreground-midground-background depth staging and oppressive pitch-black far passage",
            ],
            "PROMPT_COLORS_ELITE": [
                "deep red brick gradients and cool gray stone with desaturated shadow tones dominating the tunnel walls",
                "toxic reflective green confined strictly to puddle and grate water surfaces against dark wet ground",
                "rusty brown and oxidized gray for corroded pipe surfaces with orange-red corrosion blooms at joints",
                "small isolated bursts of bright red, yellow, and green from reflective trash packaging on the ground",
            ],
            "PROMPT_DESCRIPTION_ELITE": [
                "long extensive brick tunnel sewer system with putrid water flowing through grates and stagnant puddles on the ground",
                "misshapen twisting and broken pipes snaking along sewer tunnel sides with stray garbage scattered across the floor",
                "dimly lit sewer passage with nearly pitch-black darkness ahead in an eerie and foreboding atmosphere",
            ],
            "PROMPT_ATMOSPHERE_ELITE": [
                "oppressive dim sourceless ambient light in the near foreground fading to complete impenetrable black at tunnel depth",
                "still and lifeless sewer air with moisture-heavy atmosphere implied by wet brick surfaces and stagnant water",
                "foreboding eerie quality built entirely from darkness, decay, and accumulated refuse without any living presence",
            ],
            "PROMPT_DETAIL_ELITE": [
                "aged mortared brickwork with moisture staining, irregular repairs, crumbling mortar, and deep recessed shadow joints",
                "corroded cast-iron pipes misshapen and twisted with visible rust bloom, broken sections, and irregular wall routing",
                "individual garbage items identifiable by bright packaging color — wrappers, cans, bottles — scattered on wet dark ground",
                "grate openings in the floor surface with putrid water visible flowing beneath and pooling in low ground depressions",
            ],
            "PROMPT_DEPTH_ELITE": [
                "extreme brick arch tunnel recession with pitch-black vanishing point and atmospheric darkness deepening rapidly past mid-ground",
                "layered depth from foreground puddles and garbage to mid-ground pipe clusters to near-total darkness in far passage",
                "repeated brick arch silhouettes diminishing in size toward the vanishing point reinforcing deep perspective pull",
            ],
            "PROMPT_LIGHTING_ELITE": [
                "dim sourceless ambient glow illuminating only the near foreground with no visible light source present",
                "toxic green reflective highlights appearing only on puddle and grate water surfaces",
                "faint rust-warm catch light on corroded pipe surfaces with no clear origin",
                "near-total darkness consuming all detail beyond the tunnel mid-point in an eerie foreboding black",
            ],
            "PROMPT_SURFACE_ELITE": [
                "wet brick walls showing mineral deposits, water staining, efflorescence, and long-term moisture damage",
                "cracked uneven stone floor with low depressions collecting stagnant water and ground-level debris accumulation",
                "corroded pipe exterior surfaces showing layered rust, paint flaking, joint leakage staining, and physical deformation",
            ],
            "PROMPT_FLOW_RULES": [
                "tunnel must read as deep, long, and continuous with a clear single vanishing point pulling into total darkness",
                "puddles must show toxic reflective green tinted surfaces — still or faintly disturbed, never bright or clean",
                "pipes must appear misshapen, corroded, and irregularly routed along both side walls with broken sections visible",
                "garbage items must be identifiable by bright isolated packaging color against the dark wet ground surface",
                "no living creatures, animals, insects, rodents, humans, or plants of any kind present anywhere in the scene",
                "no nature elements, roots, moss, organic growth, or water life in any surface or puddle",
                "brickwork must show age, moisture damage, and wear without complete structural collapse",
                "lighting must be dim and sourceless in the foreground, fading to complete darkness in the far tunnel",
                "avoid clean, modern, or well-maintained sewer aesthetics — old, decayed, and long-abandoned only",
                "preserve eerie foreboding atmosphere by keeping the far tunnel passage nearly pitch black with no detail visible",
                "garbage scatter must be densest in foreground and thin progressively toward the dark mid-ground",
            ],
            "NEURAL_CAVE_SUBJECT": [
                "abandoned brick sewer tunnel receding into near-total darkness with corroded pipes and refuse-littered ground",
                "eerie first-person sewer corridor with reflective stagnant puddles, broken pipes, and garbage-scattered floor",
                "deep decaying sewer passage with dim ambient light, toxic green puddle reflections, and pitch-black far depth",
            ],
            "NEURAL_CAVE_STRUCTURE": [
                "aged mortared brick arches with moisture damage, crumbling mortar joints, and deep shadow recesses between courses",
                "corroded and misshapen pipes routed erratically along both side walls with broken sections and visible rust bloom",
                "cracked uneven stone floor with grate openings, stagnant water pooling in low depressions, and scattered refuse",
            ],
            "NEURAL_CAVE_GLOW": [
                "toxic green reflective surface glow confined only to stagnant puddles and grate water openings",
                "faint sourceless dim ambient illumination in the near foreground fading completely before mid-tunnel",
                "occasional rust-warm surface catch light on corroded pipe metal with no identifiable light source",
            ],
            "NEURAL_CAVE_MOOD": [
                "oppressive eerie abandoned sewer atmosphere with foreboding impenetrable darkness at tunnel depth",
                "unsettling lifeless stillness of forgotten decaying infrastructure with no sign of living presence",
                "dread built through contrast between visible foreground decay detail and total absence of information in the far dark",
            ],
            "NEURAL_CAVE_DEPTH": [
                "extreme linear perspective sewer tunnel with near-total black at vanishing point and dim lit near foreground only",
                "layered spatial depth from close puddles and garbage through mid-ground pipe complexity to pitch-black far arch",
                "atmospheric darkness deepening rapidly after the tunnel mid-point with no legible structure visible beyond",
            ],
            "NEURAL_CAVE_ORGANIC_PRESENCE": [
                "no living creatures, animals, insects, rodents, or humans present anywhere in the environment",
                "no plant life, roots, moss, algae, or any organic growth on any surface",
                "no sea creatures or water organisms in puddles, grates, or flowing water sections",
            ],
            "CORE_PROMPTS": [
                "first-person deep brick sewer tunnel receding into near-total darkness with stagnant reflective puddles and corroded misshapen pipes",
                "dim eerie sewer corridor with garbage-littered wet floor, twisted broken pipes on walls, and pitch-black foreboding far passage",
            ],
            "BASE_NEGATIVE_PROMPT": (
                "living creatures, animals, insects, rodents, rats, birds, fish, humans, characters, figures, silhouettes, "
                "plants, moss, algae, roots, vines, organic growth, nature elements, sea creatures, water organisms, "
                "bright overall lighting, even illumination, clean modern sewer, well-maintained infrastructure, "
                "colorful walls, painted surfaces, decorative elements, graffiti art, murals, signage, "
                "dry clean floor, empty pristine tunnel, new construction, suburban utility aesthetic, "
                "daylight, sky, outdoor elements, surface world intrusion, fantasy or sci-fi elements, "
                "cartoon style, illustrated look, cel shading, painterly rendering, stylized art, "
                "psychedelic colors, neon glow, magical lighting, unnatural color palette"
            ),
            "BASE_REFINER_PROMPT": (
                "deepen pitch-black far tunnel darkness to near-total black with hard atmospheric falloff, "
                "strengthen toxic green reflectivity on puddle and grate water surfaces only, "
                "enhance brick moisture staining, crumbling mortar, and age damage across all wall surfaces, "
                "increase pipe corrosion detail with rust bloom, deformation, and broken section visibility, "
                "clarify individual garbage item bright packaging colors against dark wet ground, "
                "preserve completely lifeless uninhabited eerie atmosphere throughout"
            ),
        },
    ),
    _pack_with_overrides(
        "hedge_tunnel",
        {
            "PROMPT_COMPOSITION_ELITE": [
                "first-person vibrant hedge tunnel corridor with tall neatly trimmed square green hedges receding into clear path depth",
                "sunlit hedge tunnel passage with organized hedge walls, bright grass floor, and open blue sky visible above",
                "wide hedge tunnel path with clear foreground-midground-background staging and junction turns visible ahead",
            ],
            "PROMPT_COLORS_ELITE": [
                "bright vibrant greens for hedge walls and grass floor with natural leaf texture variation",
                "sky blue and soft white clouds visible above hedge tops against clean daylight atmosphere",
                "light gray small stones and pale brown sticks as isolated ground detail accents on bright grass",
                "warm natural sunlight tones with soft even shadow falloff along hedge corridor walls",
            ],
            "PROMPT_DESCRIPTION_ELITE": [
                "vibrant hedge tunnel with neatly cut green hedges forming traversable paths with junctions and turns",
                "tall square trimmed hedges as corridor walls with bright grass floor and occasional stones and sticks",
                "bright sunny hedge tunnel with sky visible above and an intriguing calming atmosphere throughout",
            ],
            "PROMPT_ATMOSPHERE_ELITE": [
                "bright open calming sunny atmosphere with warm natural daylight and soft ambient shadows",
                "intriguing sense of exploration from visible path junctions and turns ahead without fear or darkness",
                "serene and organized outdoor tunnel space with clean geometry and vibrant natural color throughout",
            ],
            "PROMPT_DETAIL_ELITE": [
                "neatly trimmed flat hedge tops with clean geometric square profile and subtle natural leaf surface texture",
                "bright grass floor with small scattered light gray stones and pale brown sticks along the path",
                "visible path junction or turn ahead where hedge corridor splits into multiple traversable directions",
                "soft natural shadows cast by hedge walls onto the bright grass floor in warm midday sunlight",
            ],
            "PROMPT_DEPTH_ELITE": [
                "clear hedge corridor perspective recession with bright path floor and organized hedge walls guiding depth",
                "open sky above the hedge tops providing vertical breathing space and natural light source context",
                "junction geometry ahead creating layered depth decision points without obscuring path readability",
            ],
            "PROMPT_LIGHTING_ELITE": [
                "bright warm natural daylight as the sole light source with soft even ambient illumination throughout",
                "soft directional shadows cast from hedge walls onto grass floor indicating clear sunny day overhead",
                "no artificial lighting, no darkness, no shadows deeper than natural soft midday falloff",
            ],
            "PROMPT_SURFACE_ELITE": [
                "neatly trimmed hedge walls with organized flat top geometry and natural green leaf texture variation",
                "bright grass floor with healthy natural color and small stone and stick ground scatter detail",
                "clean geometric hedge corners and corridor edges with consistent trim line and hedge wall height",
            ],
            "PROMPT_FLOW_RULES": [
                "camera must be ground-level first-person eye-level walking perspective at all times",
                "never aerial, never top-down, never birds-eye — hedges must rise upward on both sides of the viewer",
                "hedge walls must read as tall, neatly trimmed, and square-profiled with organized geometric consistency",
                "floor must be bright healthy grass with only small stones and sticks as ground scatter detail",
                "sky must be visible above hedge tops — bright blue with soft white clouds, sunny day only",
                "path must show a junction or turn ahead to reinforce tunnel traversal and exploration feeling",
                "atmosphere must remain bright, calming, and intriguing — no darkness, fear, or ominous elements",
                "no living creatures of any kind — no insects, rodents, animals, humans, characters, or birds",
                "no water, puddles, or wet surfaces of any kind present in the environment",
                "lighting must be natural daylight only — no torches, artificial lights, or dramatic shadow work",
                "hedge color must remain vibrant saturated green — no dead, brown, or decayed hedge sections",
                "maintain open bright spatial feeling — no enclosed oppressive or claustrophobic atmosphere",
                
            ],
            "NEURAL_CAVE_SUBJECT": [
                "vibrant sunlit hedge tunnel with tall neatly trimmed green hedge walls and bright grass floor path",
                "calming first-person hedge tunnel corridor with open sky above and junction turns visible ahead",
                "organized geometric hedge tunnel passage with bright natural daylight and intriguing exploration atmosphere",
            ],
            "NEURAL_CAVE_STRUCTURE": [
                "tall square-profiled trimmed hedge walls with clean flat tops and consistent organized corridor geometry",
                "bright healthy grass floor path with small scattered light gray stones and pale brown sticks",
                "open sky above hedge tops with bright blue and soft white cloud formations in natural daylight",
            ],
            "NEURAL_CAVE_GLOW": [
                "warm natural sunlight ambient glow evenly illuminating the full hedge corridor without harsh contrast",
                "soft bright sky light spilling down from above the hedge tops onto the grass floor",
                "gentle warm highlight on upper hedge surfaces catching direct midday sunlight",
            ],
            "NEURAL_CAVE_MOOD": [
                "calming intriguing bright sunny atmosphere with organized natural geometry and open spatial feeling",
                "serene sense of peaceful exploration through a well-maintained vibrant green tunnel environment",
                "inviting and curiosity-driven mood built from visible path choices and warm natural daylight",
            ],
            "NEURAL_CAVE_DEPTH": [
                "clear open hedge corridor depth with bright grass floor and trimmed hedge walls guiding perspective",
                "junction or path turn visible ahead creating layered spatial interest without darkness or obstruction",
                "vertical sky opening above hedge tops adding depth dimension and natural light context",
            ],
            "NEURAL_CAVE_ORGANIC_PRESENCE": [
                "no living creatures of any kind — no insects, rodents, animals, humans, characters, or birds anywhere",
                "no water, puddles, streams, or wet surfaces present in any part of the environment",
                "hedge and grass are the only organic elements — healthy, vibrant, and well-maintained",
            ],
            "CORE_PROMPTS": [
                "first-person vibrant hedge tunnel with tall neatly trimmed square green hedges, bright grass floor, and open sunny sky above",
                "calming intriguing hedge tunnel corridor with junction turns ahead, small stones and sticks on grass path, bright natural daylight",
                "ground-level first-person walking view inside vibrant hedge tunnel with tall trimmed square green hedges rising on both sides, bright grass floor, open sunny sky above",
                "eye-level hedge tunnel corridor perspective with junction turns ahead, small stones and sticks on grass path, bright natural daylight, NOT aerial NOT top-down",
            ],
            "BASE_NEGATIVE_PROMPT": (
                "insects, rodents, animals, humans, characters, birds, any living creatures, "
                "water, puddles, streams, wet surfaces, moisture, "
                "darkness, shadows deeper than soft natural falloff, fear, evil, ominous atmosphere, horror elements, "
                "dead hedges, brown or decayed vegetation, overgrown untrimmed hedges, "
                "night sky, overcast sky, storm clouds, rain, fog, mist, "
                "artificial lighting, torches, lanterns, neon, glowing elements, "
                "urban elements, structures, buildings, fences, walls, non-hedge architecture, "
                "fantasy or sci-fi elements, magical effects, unnatural colors"
            ),
            "BASE_REFINER_PROMPT": (
                "strengthen vibrant saturated green on hedge walls and grass floor surfaces, "
                "enhance clean geometric flat hedge top profile and consistent trim line definition, "
                "clarify bright blue sky and soft white cloud visibility above hedge tops, "
                "refine small stone and stick ground scatter detail on bright grass floor, "
                "deepen warm natural sunlight ambient glow and soft directional shadow on corridor walls, "
                "preserve bright calming intriguing atmosphere with no darkness or living presence anywhere"
            ),
        },
    ),
     _pack_with_overrides(
        "haunted_house_hallway",
        {
            "PROMPT_COMPOSITION_ELITE": [
                "ground-level first-person eye-level view down an extremely dark narrow haunted house hallway with closed wooden doors receding into darkness",
                "immersive interior corridor perspective inside a haunted house with faded peeling wallpaper walls and candle-lit end tables flanking the path",
                "thin dark haunted hallway extending forward with closed doors on both sides, cobwebbed walls, framed paintings, and faint candlelight ahead",
            ],
            "PROMPT_COLORS_ELITE": [
                "deep muted purples dominating the dark hallway shadows and wallpaper tones with near-black recesses",
                "faded ochre and dark walnut wood tones for door frames, end tables, and floor boards",
                "warm amber and dim gold candle flame glow as the only light source cutting through deep purple-black darkness",
                "desaturated faded wallpaper pattern colors — muted yellows, dusty roses, and aged creams — partially obscured by shadow and peeling",
            ],
            "PROMPT_DESCRIPTION_ELITE": [
                "extremely dark thin haunted house hallway with mostly intact faded wallpaper and peeling spots revealing bare wall beneath",
                "cobwebs and old framed paintings decorating the walls between closed wooden doors along the receding hallway",
                "end tables holding candles and candelabras emitting faint warm light giving the hallway slight visibility",
                "extremely faint translucent ghost figure barely visible at the far end of the hallway just at the edge of perception",
            ],
            "PROMPT_ATMOSPHERE_ELITE": [
                "oppressive darkness broken only by faint warm candle glow creating an eerie claustrophobic haunted atmosphere",
                "unsettling stillness of an abandoned interior space with the suggestion of presence without explicit threat",
                "dread and unease built from deep shadow, decayed surfaces, and the barely perceptible ghost shape at the hallway end",
            ],
            "PROMPT_DETAIL_ELITE": [
                "faded wallpaper with visible repeating pattern partially obscured by age, peeling at edges and corners revealing bare plaster beneath",
                "thick cobwebs draped across upper wall corners, door frames, and painting edges with fine strand detail",
                "old framed paintings on the walls between doors — dark portraits or landscapes barely legible in low candlelight",
                "end tables with melting candles or multi-arm candelabras casting soft directional warm amber light and long shadows down the hallway",
                "closed wooden doors with faded paint, tarnished brass hardware, and dark gaps at their base suggesting rooms beyond",
            ],
            "PROMPT_DEPTH_ELITE": [
                "long narrow hallway perspective recession with doors diminishing in size toward near-total darkness at the far end",
                "faint barely-visible translucent ghost shape at the extreme far end of the hallway just within candle light reach",
                "candle light falloff deepening rapidly from warm near-foreground glow to pitch black at hallway depth",
            ],
            "PROMPT_LIGHTING_ELITE": [
                "candles and candelabras as the sole light source with warm amber flame glow and long soft shadow casting",
                "dim faint illumination giving only slight visibility — darkness dominates with light as the exception not the rule",
                "no daylight, no electric light, no windows — candlelight only with deep purple-black shadow filling all unlit space",
                "light falloff is rapid and dramatic — warm glow near candle sources fading immediately to near-total darkness beyond",
            ],
            "PROMPT_SURFACE_ELITE": [
                "faded aged wallpaper with visible dust, moisture staining, peeling edges, and partial pattern loss across wall surfaces",
                "worn wooden floorboards with age darkening, gap lines, and faint candle light reflection on polished high points",
                "wooden door surfaces with chipped paint, age cracking, tarnished metal hardware, and deep shadow in panel recesses",
            ],
            "PROMPT_GHOST_ELITE": [
                "extremely faint translucent white-gray ghost figure barely perceptible at the very end of the hallway, nearly absorbed by darkness",
                "ghost presence suggested more than shown — a vague luminous shape at threshold of visibility in the far deep shadow",
                "subtle spectral form at hallway depth with near-zero opacity, visible only as a slightly lighter region against the black",
            ],
            "PROMPT_FLOW_RULES": [
                "camera must be ground-level first-person eye-level interior perspective looking down the hallway — never aerial or top-down",
                "hallway must be narrow and thin with walls close on both sides and a low ceiling pressing down",
                "darkness must dominate — candlelight provides only faint slight visibility, not full illumination",
                "wallpaper must be faded and aged with visible peeling sections and intact patterned sections coexisting",
                "all doors must be closed — no open rooms, no visible interiors beyond doors",
                "cobwebs must appear in upper corners, door frames, and around paintings — fine and layered not cartoon-thick",
                "ghost must be extremely faint and barely visible at hallway end — suggestion of presence only, not explicit figure",
                "no living creatures of any kind — no insects, rodents, animals, humans, or characters present",
                "no water, plants, nature, or organic life of any kind",
                "no daylight, windows, or any light source other than candles and candelabras",
                "end tables must be present as candle holders — part of the hallway furniture",
                "paintings must be on the walls between peeled wallpaper sections — dark and barely legible in candlelight",
            ],
            "NEURAL_CAVE_SUBJECT": [
                "extremely dark narrow haunted house hallway with faded peeling wallpaper, closed wooden doors, and faint candle-lit end tables",
                "eerie first-person haunted corridor with cobwebbed walls, old framed paintings, candelabras, and barely visible ghost at hallway end",
                "dim candlelit haunted house interior hallway receding into near-total darkness with spectral presence at far depth",
            ],
            "NEURAL_CAVE_STRUCTURE": [
                "narrow hallway with faded patterned wallpaper walls — intact in sections, peeling at edges — and worn wooden floor boards",
                "closed wooden doors with tarnished hardware receding along both sides of the hallway into darkness",
                "end tables positioned along hallway walls bearing candles and candelabras with cobwebs and paintings between doors",
            ],
            "NEURAL_CAVE_GLOW": [
                "warm amber candle flame glow as the only light — dim, faint, and localized with immediate falloff into deep shadow",
                "soft directional candlelight casting long shadows down the hallway floor and up the wallpapered walls",
                "faint ghostly luminescence at the extreme far end of the hallway barely distinguishable from darkness",
            ],
            "NEURAL_CAVE_MOOD": [
                "deeply eerie and unsettling haunted interior atmosphere with oppressive darkness and faint spectral suggestion",
                "dread built from narrow space, decayed surfaces, closed doors, and the barely perceptible ghost at hallway depth",
                "haunted house stillness — abandoned, decayed, and quietly threatening without explicit horror",
            ],
            "NEURAL_CAVE_DEPTH": [
                "long narrow hallway recession with doors diminishing toward near-total black and faint ghost shape at vanishing depth",
                "candle light warmth in foreground fading rapidly to cold purple-black darkness at hallway mid-point and beyond",
                "layered depth from close wallpaper texture through mid-ground doors and end tables to far ghost suggestion",
            ],
            "NEURAL_CAVE_ORGANIC_PRESENCE": [
                "no living creatures of any kind — no insects, rodents, animals, humans, or characters anywhere in the scene",
                "no plants, nature, water, or organic life present on any surface or in any detail",
                "cobwebs are the only organic-adjacent detail — present as abandoned remnants, not active or inhabited",
            ],
            "CORE_PROMPTS": [
                "ground-level first-person eye-level view down an extremely dark narrow haunted house hallway, faded peeling wallpaper, closed wooden doors, end tables with candles and candelabras, cobwebs and old paintings on walls",
                "dim candlelit haunted interior corridor receding into near-total darkness, deep muted purples and faded ochre wood tones, extremely faint barely visible ghost at far hallway end, no living presence of any kind",
            ],
            "BASE_NEGATIVE_PROMPT": (
                "insects, rodents, animals, humans, characters, living creatures of any kind, "
                "plants, nature, water, organic life, moss, roots, "
                "open doors, visible room interiors, daylight, windows, sunlight, "
                "electric lighting, modern fixtures, bright illumination, even lighting, "
                "clean undamaged wallpaper, new or well-maintained surfaces, "
                "aerial view, top-down view, birds-eye view, exterior house view, "
                "explicit clear ghost figure, fully visible apparition, cartoon ghost, "
                "bright colors, saturated palette, vivid hues, neon, fantasy magic effects, "
                "cartoon style, illustrated look, painterly rendering, stylized art, "
                "gore, blood, explicit horror imagery, jump scare elements"
            ),
            "BASE_REFINER_PROMPT": (
                "deepen dark purple-black shadow dominance throughout hallway with candle glow as only light exception, "
                "enhance faded wallpaper age detail — peeling edges, dust, staining, and partial pattern visibility, "
                "strengthen cobweb fine strand detail in corners and around door frames and paintings, "
                "refine warm amber candle flame glow with rapid falloff into deep surrounding shadow, "
                "reduce ghost visibility to absolute threshold — faint luminous suggestion only at hallway far end, "
                "preserve narrow claustrophobic hallway geometry with closed doors and end tables intact"
            ),
        },
    ),
    _pack_with_overrides(
    "uncanny_cave_depth", {
    "PROMPT_COMPOSITION_ELITE": [
        "first-person descent through a vast cave chamber with no obvious end",
        "distributed geological weight with layered depth and no single dominant focal point",
        "receding cavern passages framed by hanging stalactites and rising stone forms",
    ],
    "PROMPT_COLORS_ELITE": [
        "cold mineral grays, wet charcoal stone, faint blue-black shadow, and subtle silver moisture highlights",
        "desaturated limestone tones with dark reflective pools and pale mineral glints",
        "dim earthy cave hues interrupted by sparse cold shimmer from dripping water and slick rock",
    ],
    "SCIENCE_CUES_ELITE": [
        "natural karst erosion patterns with believable mineral deposition and moisture accumulation",
        "layered calcite textures, realistic cave humidity, and slow water-driven surface shaping",
        "geological formations shaped by time, pressure, and dripping mineral-rich water",
    ],
    "PROMPT_PAREIDOLIA_ELITE": [
        "rock formations that almost resemble watching faces when viewed too long",
        "subtle human-like impressions hidden in converging stalactite and shadow patterns",
        "near-recognizable figures suggested by mineral silhouettes and cave depth",
    ],
    "PROMPT_NEUROCHEM_ELITE": [
        "slow suspense created through repeating depth cues and uncertain darkness",
        "introspective unease caused by soft echoing emptiness and ambiguous forms",
        "mysterious stillness that encourages deep visual scanning of every shadowed recess",
    ],
    "PROMPT_TEMPORAL_ELITE": [
        "water droplets suspended in slow intermittent motion from the cave ceiling",
        "ripples expanding across shallow puddles in near-total silence",
        "ancient stillness broken only by occasional dripping water and shifting mist",
    ],
    "PROMPT_EDGE_ELITE": [
        "the cave feels natural at first glance but increasingly wrong the longer it is studied",
        "meaningful geometry seems buried inside the natural stone structure",
        "the environment appears ancient, untouched, and subtly aware",
    ],
    "PROMPT_ARCHETYPES_ELITE": [
        "a primal underworld chamber hidden beneath the world",
        "a place of descent, isolation, and buried memory",
        "an ancient hollow that feels like both refuge and trap",
    ],
    "PROMPT_SCALE_ELITE": [
        "vast enough to feel cathedral-like yet intimate enough to feel suffocating",
        "towering stone forms contrasted with tiny droplets and fine mineral textures",
        "scale ambiguity between narrow tunnel compression and massive hidden voids",
    ],
    "PROMPT_FLOW_RULES": [
        "prioritize damp realism and oppressive depth over fantasy spectacle",
        "maintain darkness as the dominant force with only sparse reflective highlights",
        "favor natural cave continuity, believable geology, and grounded atmospheric tension",
        "keep motion minimal, slow, and environmental rather than dramatic",
        "avoid obvious landmarks and let depth unfold gradually",
    ],
    "CAVE_SUBJECT": [
        "immersive first-person cave system with deep passage and towering formations",
        "uncanny subterranean cavern with wet stone, mineral structures, and receding darkness",
        "vast natural cave chamber with dripping ceilings and layered shadow depth",
    ],
    "CAVE_STRUCTURE": [
        "dense stalactites above, thick stalagmites below, slick rock walls, and broken stone ledges",
        "arching cave ceiling with mineral teeth, uneven ground, shallow puddles, and split passages",
        "erosion-carved walls, narrow side hollows, reflective wet surfaces, and deep central void",
    ],
    "CAVE_GLOW": [
        "minimal cold reflection from water and damp stone surfaces",
        "faint mineral sheen catching on moisture and calcite deposits",
        "subtle dim light scattered through mist and reflective puddles only",
    ],
    "CAVE_PAREIDOLIA": [
        "rare face-like impressions formed by rock and shadow convergence",
        "watchful silhouettes hinted by stalagmite clusters in the distance",
        "near-human topology hidden in fractured cave walls",
    ],
    "CAVE_MOOD": [
        "oppressive, ancient, isolating, and mesmerizing atmosphere",
        "primal dread mixed with awe and exploratory tension",
        "quiet, uncanny, and emotionally heavy subterranean stillness",
    ],
    "CAVE_SCIENCE": [
        "believable geological realism with karst formations and mineral deposition",
        "erosion-driven natural architecture with moisture physics and realistic cave acoustics implied",
        "strong environmental plausibility with subtle uncanny distortion",
    ],
    "CAVE_MORPH": [
        "long limestone teeth descending into clustered mineral pillars",
        "wet cave ribs opening into broader chambers and compressed side channels",
        "smooth calcite bulges and broken stone spires layered across depth",
    ],
    "CAVE_DEPTH": [
        "first-person cave depth with central passage and receding black chamber beyond",
        "layered underground perspective with foreground mineral forms and distant darkness",
        "immersive tunnel-like depth opening into a larger uncertain cavern interior",
    ],
    "CAVE_ORGANIC_PRESENCE": [
        "subtle mist, moisture, and faint biological residue with no visible creatures",
        "rare fungal staining and mineral wetness integrated naturally into stone recesses",
        "light traces of life implied only through dampness and texture variation",
    ],
    "CORE_PROMPTS": [
        "first-person uncanny cave passage, dripping ceiling, deep central darkness",
        "towering stalactites and stalagmites, wet stone floor, strong layered subterranean depth",
    ],
    "ORGANIC_SHORT": [
        "subtle cave mist in recesses, non-dominant",
        "rare face-like rock suggestion in shadowed mineral walls",
        "faint fungal staining near puddles, understated",
        "soft water ripple detail across shallow pooled stone",
    ],
    "BASE_REFINER_PROMPT": (
        "enhance realistic cave geology, refine wet stone reflections, sharpen mineral textures, "
        "improve dripping moisture detail, subtle uncanny pareidolia, deep immersive subterranean depth"
    ),
    "BASE_NEGATIVE_PROMPT": (
        "city, buildings, urban street, laboratory, furniture, sci-fi corridor, "
        "clean architecture, symmetrical hallway, geometric room, bright daylight, sky, sun, "
        "cartoon, fantasy crystals, lava, glowing magic, oversaturated colors, stylized art, "
        "text, watermark, logo, flat pattern, top-down view, aerial view, creatures, crowd, gore"
    ),
        },
    ),
    _pack_with_overrides(
    "uncanny_abandoned_city", {
    "PROMPT_COMPOSITION_ELITE": [
        "first-person street-level urban desolation with long sightlines and layered building depth",
        "distributed decay across alleys, street edges, windows, and distant structures with no dominant center",
        "immersive abandoned city corridor framed by tall silent buildings and empty pavement",
    ],
    "PROMPT_COLORS_ELITE": [
        "faded concrete gray, dirty sodium amber, rust brown, weak cyan reflections, and deep urban shadow",
        "desaturated city tones with cold window blacks, cracked asphalt, and intermittent dead neon residue",
        "weathered urban palette of soot, dust, rust, and weak flickering light",
    ],
    "SCIENCE_CUES_ELITE": [
        "realistic urban decay with water damage, oxidation, dust accumulation, and structural wear",
        "believable city abandonment marked by broken signage, cracked pavement, and invasive stillness",
        "environmental realism driven by erosion, neglect, collapse, and long-term exposure",
    ],
    "PROMPT_PAREIDOLIA_ELITE": [
        "dark windows and alley geometry almost form watching faces",
        "vacant building facades imply silent observers through shape and shadow",
        "human presence is suggested only through layout, emptiness, and near-recognizable silhouettes",
    ],
    "PROMPT_NEUROCHEM_ELITE": [
        "quiet urban dread built through stillness, absence, and repeating empty structures",
        "tension created by the sense that the city should contain movement but does not",
        "deep exploratory unease encouraged by alleys, broken visibility, and uncertain distance",
    ],
    "PROMPT_TEMPORAL_ELITE": [
        "occasional paper or dust drifting through the street in weak air currents",
        "intermittent distant flicker from a failing sign or unstable light source",
        "a city frozen in prolonged abandonment with almost no motion",
    ],
    "PROMPT_EDGE_ELITE": [
        "the city feels abandoned but not fully empty",
        "the environment implies recent absence without showing any clear cause",
        "something about the silence feels staged rather than natural",
    ],
    "PROMPT_ARCHETYPES_ELITE": [
        "a dead metropolis preserved like a memory shell",
        "an urban graveyard of human systems and routines",
        "a silent maze of civilization after presence has vanished",
    ],
    "PROMPT_SCALE_ELITE": [
        "towering buildings contrasted with small debris and cracked pavement detail",
        "street-level intimacy against massive urban verticality",
        "dense environmental scale from nearby grit to distant skyline voids",
    ],
    "PROMPT_FLOW_RULES": [
        "prioritize loneliness, depth, and realism over action or spectacle",
        "keep streets empty and emotionally cold",
        "favor believable decay and subtle environmental storytelling",
        "let darkness and emptiness carry most of the tension",
        "avoid obvious horror clichés and preserve ambiguity",
    ],
    "CITY_SUBJECT": [
        "immersive first-person abandoned city street with uncanny silence and layered urban decay",
        "deserted alley and road corridor between tall empty buildings",
        "street-level post-abandonment cityscape with broken windows and silent depth",
    ],
    "CITY_STRUCTURE": [
        "cracked asphalt, narrow alleys, shattered windows, faded signage, and rusted storefront frames",
        "tall buildings with dark interiors, debris-lined sidewalks, dead streetlights, and recessed entryways",
        "empty road with scattered trash, broken glass, sagging power lines, and shadowed side alleys",
    ],
    "CITY_GLOW": [
        "faint sodium-like streetlight residue and weak intermittent reflections",
        "minimal failing urban illumination from distant flicker only",
        "subtle dead neon traces and cold reflections on wet or dusty surfaces",
    ],
    "CITY_PAREIDOLIA": [
        "window grids and alley mouths suggest watching faces",
        "facade shadows imply human silhouettes without showing anyone",
        "building geometry creates subconscious observer-like impressions",
    ],
    "CITY_MOOD": [
        "lonely, suspended, uncanny, and emotionally hollow atmosphere",
        "abandoned urban quiet with latent tension and lingering unease",
        "civilization emptied out but somehow still expectant",
    ],
    "CITY_SCIENCE": [
        "realistic environmental weathering, dust, oxidation, and structural neglect",
        "plausible urban abandonment with physical signs of time and exposure",
        "grounded city decay with subtle psychological uncanniness",
    ],
    "CITY_MORPH": [
        "narrow alleys opening into wider empty street segments",
        "stacked facades and recessed windows forming canyon-like city depth",
        "fragmented signage, rusted frames, and cracked pavement guiding perspective",
    ],
    "CITY_DEPTH": [
        "first-person street depth with receding facades and a dim vanishing point",
        "immersive alley-to-street perspective with multiple visibility breaks",
        "deep urban corridor framed by tall buildings and empty distance",
    ],
    "CITY_ORGANIC_PRESENCE": [
        "rare weeds through pavement cracks, non-dominant",
        "dust buildup and weather staining with no active life visible",
        "light traces of nature barely reclaiming the city edges",
    ],
    "CORE_PROMPTS": [
        "first-person abandoned city street, empty alley, cracked pavement, dark windows",
        "layered urban decay, tall silent buildings, strong street-level depth, uncanny stillness",
    ],
    "ORGANIC_SHORT": [
        "rare weeds through pavement cracks, understated",
        "dust and paper drifting lightly through the street",
        "faint silhouette-like shadow in a far window, ambiguous",
        "dead signage and broken glass detail, non-dominant",
    ],
    "BASE_REFINER_PROMPT": (
        "enhance urban decay realism, refine cracked asphalt, weathered concrete, broken glass, "
        "deep street perspective, subtle uncanny emptiness, silent abandoned city atmosphere"
    ),
    "BASE_NEGATIVE_PROMPT": (
        "forest, cave, underwater, laboratory interior, clean modern city, crowded street, cars in motion, "
        "people, living creatures, bright daylight, blue sky, lush plants, fantasy glow, neon cyberpunk, "
        "cartoon, stylized painting, text, watermark, logo, aerial view, top-down view, gore"
    ),
},
    ),
    _pack_with_overrides(
        "uncanny_abandoned_lab",{
    "PROMPT_COMPOSITION_ELITE": [
        "first-person interior research facility depth with layered workstations, containment forms, and receding corridors",
        "distributed technical detail across benches, monitors, cables, and sealed doors without a single dominant anchor",
        "immersive laboratory decay with sterile geometry corrupted by neglect and silence",
    ],
    "PROMPT_COLORS_ELITE": [
        "cold fluorescent remnants, faded white surfaces, oxidized metal, desaturated teal, and deep surgical shadow",
        "sterile lab tones broken by grime, warning colors, and weak failing illumination",
        "muted industrial palette of steel gray, dead green-blue monitor residue, and yellowed plastics",
    ],
    "SCIENCE_CUES_ELITE": [
        "plausible research equipment, containment architecture, warning labels, and utility infrastructure",
        "realistic signs of abandonment including dust, corrosion, disconnected systems, and failed lighting",
        "credible laboratory materials like glass, steel, polymers, sealed panels, and old instrumentation",
    ],
    "PROMPT_PAREIDOLIA_ELITE": [
        "cabinet lines, hanging cables, and reflections almost form figures in peripheral view",
        "containment chambers and broken monitor reflections imply presence without showing anyone",
        "the lab layout creates subconscious observer-like impressions through symmetry and shadow",
    ],
    "PROMPT_NEUROCHEM_ELITE": [
        "sterile dread caused by the contrast between scientific order and sudden abandonment",
        "tension created by the sense that something important ended here too quickly",
        "obsessive detail invites close scanning of equipment, glass, and dim recesses",
    ],
    "PROMPT_TEMPORAL_ELITE": [
        "occasional fluorescent flicker or unstable monitor residue in the dark",
        "fine dust resting motionless over equipment and surfaces",
        "still air and static silence preserved like the moment after an evacuation",
    ],
    "PROMPT_EDGE_ELITE": [
        "the lab feels documented, controlled, and yet subtly contaminated by the unknown",
        "something appears missing from the room in a way that is impossible to define",
        "the environment suggests discovery, failure, and concealment all at once",
    ],
    "PROMPT_ARCHETYPES_ELITE": [
        "a forbidden research site left behind after crossing a boundary",
        "a sterile chamber of knowledge corrupted by what it tried to contain",
        "a place where control failed quietly rather than violently",
    ],
    "PROMPT_SCALE_ELITE": [
        "small instrument detail contrasted with long corridor and chamber depth",
        "tight workstation clutter against larger sealed architectural volumes",
        "fine scientific textures embedded in wide sterile emptiness",
    ],
    "PROMPT_FLOW_RULES": [
        "prioritize realism, silence, and abandonment over sci-fi spectacle",
        "maintain a grounded research-facility feel with believable material detail",
        "let failing light and environmental stillness carry the unease",
        "avoid futuristic fantasy tech and keep the lab physically plausible",
        "preserve ambiguity about what happened here",
    ],
    "LAB_SUBJECT": [
        "immersive first-person abandoned research laboratory with decayed sterile realism",
        "silent underground lab interior with workstations, containment glass, and failing lights",
        "uncanny deserted laboratory space with sealed doors and forgotten experiments",
    ],
    "LAB_STRUCTURE": [
        "metal benches, broken monitors, dangling cables, containment chambers, and sealed warning-marked doors",
        "scattered papers, glass panels, oxidized equipment, overhead fixtures, and recessed corridors",
        "laboratory workstations with old instruments, cracked display screens, and dusty metal surfaces",
    ],
    "LAB_GLOW": [
        "weak fluorescent flicker and faint dead monitor glow only",
        "cold residual illumination across dusty sterile surfaces",
        "subtle reflected light on glass, steel, and plastic with darkness dominating",
    ],
    "LAB_PAREIDOLIA": [
        "cabinet reflections and shadows create almost human forms",
        "glass containment shapes imply presence without clarity",
        "observer-like symmetry hidden within sterile lab geometry",
    ],
    "LAB_MOOD": [
        "sterile, tense, secretive, and emotionally unnerving atmosphere",
        "clinical silence mixed with failure and hidden consequence",
        "controlled space transformed into an uncanny relic",
    ],
    "LAB_SCIENCE": [
        "believable lab materials, layouts, equipment logic, and infrastructure wear",
        "physically plausible research environment with abandonment details",
        "grounded technological realism shaped by neglect and silence",
    ],
    "LAB_MORPH": [
        "rows of workstations leading toward sealed chambers and secondary corridors",
        "glass and steel forms layered with cables, labels, and dust-coated surfaces",
        "tight laboratory geometry interrupted by broken displays and darkened recesses",
    ],
    "LAB_DEPTH": [
        "first-person lab depth with foreground instruments and receding sterile passage",
        "immersive interior perspective through workstations toward a dim sealed door",
        "layered containment room depth with distant corridor extension",
    ],
    "LAB_ORGANIC_PRESENCE": [
        "no active life, only dust and staining from long abandonment",
        "rare residue or discoloration on surfaces, non-dominant",
        "faint signs of contamination implied only through texture and atmosphere",
    ],
    "CORE_PROMPTS": [
        "first-person abandoned lab interior, broken monitors, containment glass, sealed doors",
        "sterile decay, weak fluorescent flicker, layered scientific detail, uncanny silence",
    ],
    "ORGANIC_SHORT": [
        "dust-coated equipment and papers, understated",
        "faint contamination-like staining, ambiguous",
        "subtle reflection in containment glass suggesting presence, non-explicit",
        "hanging cables and warning labels, non-dominant",
    ],
    "BASE_REFINER_PROMPT": (
        "enhance sterile laboratory realism, refine metal and glass materials, improve dust, corrosion, "
        "failing fluorescent detail, subtle uncanny scientific atmosphere, deep research-facility perspective"
    ),
    "BASE_NEGATIVE_PROMPT": (
        "forest, cave, ocean, city street, fantasy laboratory, advanced holograms, robots, alien tech, "
        "bright clean hospital, active scientists, humans, creatures, daylight, windows with sunlight, "
        "cartoon, painterly, stylized art, text, watermark, logo, top-down, aerial, gore"
    ),
},
    ),
    _pack_with_overrides(
        "uncanny_forest_depth",{
    "PROMPT_COMPOSITION_ELITE": [
        "first-person path through a dense forest with heavy depth layering and no clear endpoint",
        "distributed tree density, root systems, mist, and branch complexity across the full frame",
        "immersive woodland corridor with repeated trunks and narrowing visibility",
    ],
    "PROMPT_COLORS_ELITE": [
        "deep moss green, wet bark brown, gray mist, muted earth, and cold pale light filtering through canopy",
        "desaturated woodland tones with shadow-heavy foliage and soft moisture highlights",
        "dim organic palette of roots, bark, leaf decay, and atmospheric fog",
    ],
    "SCIENCE_CUES_ELITE": [
        "believable forest ecology with layered undergrowth, bark texture, root spread, and moisture haze",
        "natural woodland density shaped by growth competition, decay, and light occlusion",
        "realistic environmental structure with tree spacing, forest floor debris, and atmospheric diffusion",
    ],
    "PROMPT_PAREIDOLIA_ELITE": [
        "tree bark and branch intersections almost form faces when viewed too long",
        "shadowed trunks suggest human silhouettes without becoming explicit figures",
        "watchful impressions emerge from repeated vertical forms and mist gaps",
    ],
    "PROMPT_NEUROCHEM_ELITE": [
        "calm and unease layered together through repetition, depth, and soft concealment",
        "slow visual scanning encouraged by tangled roots, distant mist, and hidden forms",
        "a hypnotic but increasingly wrong woodland stillness",
    ],
    "PROMPT_TEMPORAL_ELITE": [
        "slight fog drift and subtle leaf movement in near-silent air",
        "suspended particles and moisture hanging between trunks",
        "still woodland atmosphere broken only by tiny natural motion",
    ],
    "PROMPT_EDGE_ELITE": [
        "the forest looks natural but arranged with impossible emotional precision",
        "something about the spacing of the trees feels intentional",
        "the environment suggests intelligence without any visible entity",
    ],
    "PROMPT_ARCHETYPES_ELITE": [
        "an ancient threshold forest between known and unknown space",
        "a place of wandering, concealment, and quiet judgment",
        "a living maze of memory and instinct",
    ],
    "PROMPT_SCALE_ELITE": [
        "massive trunks and canopy depth contrasted with tiny moss, roots, and leaf detail",
        "intimate forest-floor texture beneath towering vertical woodland forms",
        "multi-scale natural density from bark pores to distant tree walls",
    ],
    "PROMPT_FLOW_RULES": [
        "prioritize immersive density and subtle uncanniness over fantasy exaggeration",
        "keep lighting soft, filtered, and partially obscured",
        "favor natural realism with psychological distortion rather than overt surrealism",
        "let repetition of trees and limited visibility create the tension",
        "avoid bright magical color and preserve organic groundedness",
    ],
    "FOREST_SUBJECT": [
        "immersive first-person uncanny forest path with dense trees and receding mist",
        "deep woodland corridor with twisted roots, layered trunks, and dim filtered light",
        "natural forest interior that feels calm, ancient, and quietly wrong",
    ],
    "FOREST_STRUCTURE": [
        "closely spaced trunks, heavy roots, mossy ground, broken branches, and narrowing paths",
        "dense canopy overhead, leaf litter below, mist between tree lines, and tangled undergrowth",
        "old bark textures, twisted branch forms, uneven forest floor, and soft depth-obscuring haze",
    ],
    "FOREST_GLOW": [
        "faint pale light filtered weakly through the canopy",
        "soft ambient moisture glow across mist and wet bark",
        "minimal natural highlights on roots, leaves, and moss in low woodland light",
    ],
    "FOREST_PAREIDOLIA": [
        "bark fissures and branches imply watchful faces",
        "distant trunk groupings resemble standing figures",
        "observer-like shapes emerge from fog gaps and overlapping trees",
    ],
    "FOREST_MOOD": [
        "calming, ancient, eerie, and psychologically immersive atmosphere",
        "nature-based stillness infused with subtle dread",
        "quiet woodland beauty made uncanny through repetition and concealment",
    ],
    "FOREST_SCIENCE": [
        "believable forest ecology, root systems, bark growth, and moisture conditions",
        "grounded woodland realism with atmospheric haze and natural layering",
        "plausible environmental density with emotionally uncanny presentation",
    ],
    "FOREST_MORPH": [
        "tight tree corridors opening into slightly wider fogged clearings",
        "root systems crossing the path and guiding the eye deeper into the woods",
        "branch lattices and trunk repetition creating natural woodland compression",
    ],
    "FOREST_DEPTH": [
        "first-person forest depth with layered trunks and disappearing path ahead",
        "immersive woodland perspective with foreground roots and receding mist corridors",
        "deep canopy-shadowed forest with multiple depth bands of tree density",
    ],
    "FOREST_ORGANIC_PRESENCE": [
        "moss, ferns, and subtle fungal traces integrated naturally into the forest floor",
        "small plants and decaying leaf matter, non-dominant and realistic",
        "light biological abundance without any visible animals or active creatures",
    ],
    "CORE_PROMPTS": [
        "first-person uncanny forest path, dense trees, roots, mist, deep woodland depth",
        "natural but unsettling forest interior, layered trunks, filtered light, hidden forms",
    ],
    "ORGANIC_SHORT": [
        "moss and roots across the ground, understated",
        "soft fog drifting between trunks, non-dominant",
        "rare face-like bark pattern, ambiguous",
        "light fungal traces near fallen wood, subtle",
    ],
    "BASE_REFINER_PROMPT": (
        "enhance forest realism, refine bark texture, moss, roots, mist layering, filtered canopy light, "
        "subtle uncanny pareidolia, deep immersive woodland perspective"
    ),
    "BASE_NEGATIVE_PROMPT": (
        "city, buildings, streets, laboratory, cave interior, underwater scene, bright fantasy forest, "
        "glowing magic, fairies, animals, humans, creatures, vibrant saturated colors, cartoon, stylized art, "
        "text, watermark, logo, aerial view, top-down view, gore"
    ),
},
    ),
    _pack_with_overrides(
    "uncanny_abyssal_underwater",{
    "PROMPT_COMPOSITION_ELITE": [
        "first-person deep underwater descent through wreckage and structural ruins with vast empty distance",
        "distributed abyssal detail across drifting particles, broken structures, seabed debris, and open dark water",
        "immersive oceanic depth with no stable horizon and a looming distant void",
    ],
    "PROMPT_COLORS_ELITE": [
        "dark blue-black water, cold green depth, oxidized wreck metal, pale silt, and faint bioluminescent residue",
        "desaturated abyssal palette with weak cyan diffusion and near-black distance",
        "deep ocean tones of pressure-darkened water, rust, sediment, and sparse ghostly light",
    ],
    "SCIENCE_CUES_ELITE": [
        "realistic underwater visibility loss, suspended particulate matter, pressure-darkened depth, and wreck corrosion",
        "believable marine ruin textures with encrusted surfaces, sediment drift, and waterborne light diffusion",
        "plausible abyssal environment with scale loss, low contrast distance, and slow current movement",
    ],
    "PROMPT_PAREIDOLIA_ELITE": [
        "large distant forms in the water almost resemble an observing creature",
        "broken wreck geometry and drifting silt imply faces and bodies without clarity",
        "a massive silhouette may or may not exist beyond the visibility threshold",
    ],
    "PROMPT_NEUROCHEM_ELITE": [
        "deep isolation and awe created by open black water beyond visible structure",
        "tension caused by uncertain scale and the possibility of something moving in the far distance",
        "slow hypnotic dread shaped by drifting silt, silence, and limited visibility",
    ],
    "PROMPT_TEMPORAL_ELITE": [
        "silt clouds drifting slowly in the current",
        "tiny suspended particles moving continuously through dim water",
        "occasional large distant motion implied rather than clearly shown",
    ],
    "PROMPT_EDGE_ELITE": [
        "the ocean feels empty except for one impossible distant implication",
        "the environment suggests a watcher hidden at the edge of visibility",
        "the ruins seem too small compared to whatever the darkness might contain",
    ],
    "PROMPT_ARCHETYPES_ELITE": [
        "an abyssal graveyard beneath the known world",
        "a drowned memory of civilization swallowed by depth",
        "the primal fear of being observed in vast dark water",
    ],
    "PROMPT_SCALE_ELITE": [
        "fine floating particles contrasted with massive wreck sections and impossible distant scale",
        "close corroded detail against endless water depth",
        "micro suspended debris embedded within vast oceanic emptiness",
    ],
    "PROMPT_FLOW_RULES": [
        "prioritize pressure, depth, and visibility loss over bright marine beauty",
        "keep the water dark, heavy, and spatially uncertain",
        "favor slow current motion and drifting particulate detail",
        "preserve ambiguity around the distant creature or silhouette",
        "avoid colorful reef aesthetics and maintain abyssal dread",
    ],
    "UNDERWATER_SUBJECT": [
        "immersive first-person deep underwater ruin with wreckage and open abyss",
        "dark oceanic environment with destroyed structures, sediment drift, and distant looming uncertainty",
        "abyssal seabed scene with shipwreck debris and a possible massive silhouette far away",
    ],
    "UNDERWATER_STRUCTURE": [
        "broken ship hull fragments, collapsed beams, sediment-covered debris, and partial submerged structures",
        "ruined underwater architecture or wreckage lying across the seabed with open dark water beyond",
        "corroded metal, shattered framework, scattered remains, and dim depth receding into black",
    ],
    "UNDERWATER_GLOW": [
        "faint filtered cyan-blue light with near-black distance",
        "minimal weak bioluminescent or particulate glow in dark water",
        "soft cold underwater diffusion with little direct illumination",
    ],
    "UNDERWATER_PAREIDOLIA": [
        "a huge distant silhouette suggested far beyond the wreckage",
        "observer-like forms emerging from darkness and sediment layers",
        "massive creature-like ambiguity at the edge of visibility",
    ],
    "UNDERWATER_MOOD": [
        "isolating, vast, oppressive, and quietly terrifying atmosphere",
        "deep-sea awe fused with uncertainty and low-visibility dread",
        "silent pressure-heavy emptiness with something implied in the distance",
    ],
    "UNDERWATER_SCIENCE": [
        "realistic water attenuation, sediment motion, corrosion, and abyssal lighting behavior",
        "grounded underwater physics with believable structure decay and depth obscuration",
        "plausible deep-ocean environmental realism with subtle uncanny scale cues",
    ],
    "UNDERWATER_MORPH": [
        "wreck fragments opening into broader abyssal emptiness",
        "broken structures partly buried in sediment with beams extending into dark water",
        "collapsed marine ruins framing a distant black depth field",
    ],
    "UNDERWATER_DEPTH": [
        "first-person underwater depth with foreground wreckage and distant open void",
        "immersive abyssal perspective through drifting particles toward a dark unreachable distance",
        "layered deep-sea space with near debris, middle silt field, and far black water",
    ],
    "UNDERWATER_ORGANIC_PRESENCE": [
        "light marine growth and encrustation on wreck surfaces, non-dominant",
        "subtle bioluminescent residue and sparse underwater life traces only",
        "minimal living detail to preserve emptiness and dread",
    ],
    "CORE_PROMPTS": [
        "first-person deep underwater wreck, dark abyss, drifting silt, distant massive silhouette",
        "destroyed underwater structure, corroded debris, heavy depth, low visibility, uncanny ocean dread",
    ],
    "ORGANIC_SHORT": [
        "subtle marine encrustation on metal, understated",
        "drifting silt and suspended particles everywhere",
        "faint distant creature-like silhouette, ambiguous",
        "minimal weak bioluminescent traces, non-dominant",
    ],
    "BASE_REFINER_PROMPT": (
        "enhance underwater realism, refine suspended particles, corrosion, wreck detail, depth haze, "
        "low-visibility abyssal atmosphere, subtle distant massive silhouette"
    ),
    "BASE_NEGATIVE_PROMPT": (
        "forest, city, cave, laboratory, bright coral reef, tropical fish, sunlight beams, surface ocean, beach, "
        "humans, divers, submarines, cartoon sea life, fantasy sea monsters, oversaturated colors, stylized art, "
        "text, watermark, logo, aerial view, top-down view, gore"
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
# 6 - 11
ACTIVE_PROMPT_PACK: Union[str, int] = "random"

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
