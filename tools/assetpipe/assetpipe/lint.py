"""Prompt linter: ART_DIRECTION.md sections 3, 4, 5, 7 and the section 8 self-review, as code.

Errors fail ``assetpipe prompts`` / ``assetpipe lint``; warnings are printed but do not fail.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

from .packs import PROMPT_KINDS, ROLES, SETUP_MATCHES, TONES, UPSCALERS, Pack, Wallpaper

LANDSCAPE_WORDS = (120, 220)

OPENING = "A 16:9 landscape desktop wallpaper."
SAFE_ZONE = (
    "The left 15% and the bottom 8% stay calm and low-detail (desktop icons and taskbar go there). "
    "Keep every important element inside the central 80% of the width and the middle 70% of the height "
    "so the image can be cropped to other screen shapes."
)
TECHNICAL_START = "Technical: highest available resolution;"
EXCLUSION_START = "Strictly no text, letters, numbers, logos, symbols,"
ULTRAWIDE_START = "Extend this exact image into an ultra-wide 3:1 panorama"
PORTRAIT_START = "Using the attached image as the style and content reference, recompose the same scene as a 9:16"

# Phrases section 7.1 tells us never to use, plus living-artist / studio style references (section 5).
BANNED_PHRASES = (
    "4k", "8k", "ultra hd", "ultra-hd", "uhd", "trending on", "artstation", "masterpiece",
    "ghibli", "pixar", "disney", "screenshot from", "in the style of", "unreal engine", "octane render",
)

# Studios, publishers, platforms and launchers: never in prompt text (section 5).
STUDIO_NAMES = (
    "valve", "riot", "epic games", "blizzard", "rockstar", "ubisoft", "mojang", "microsoft", "nintendo",
    "bethesda", "activision", "electronic arts", "bungie", "mihoyo", "hoyoverse", "cd projekt", "larian",
    "fromsoftware", "capcom", "pocketpair", "game freak", "supergiant", "team cherry", "digital extremes",
    "playground games", "bandai", "square enix", "grinding gear", "embark", "behaviour", "marvel",
    "xbox", "playstation", "twitch", "discord",
)

# Generic words that appear in game titles but are ordinary English; they are fine in prompts.
TITLE_STOPWORDS = frozenset(
    "the of and a an in on to for with from impact star rail road horizon lost dead red rust wild wilds "
    "company hunter hunters grand theft auto shift legends league strike counter global offensive "
    "battle ground grounds sea thieves truck simulator euro silk song hollow knight black myth "
    "dream day night city line force war fall rider rain".split()
)

# Proper-noun tokens that are NOT ordinary words and must never leak, keyed by nothing: a global list of the
# worlds, characters, factions and modes in the seed catalog. Kept deliberately short and unambiguous.
PROPER_NOUNS = (
    "counter-strike", "valorant", "league of legends", "fortnite", "minecraft", "roblox", "apex legends",
    "overwatch", "rocket league", "grand theft auto", "gta", "marvel rivals", "call of duty", "warzone",
    "dota", "rainbow six", "siege x", "cyberpunk 2077", "night city", "elden ring", "lands between", "erdtree",
    "genshin", "teyvat", "pubg", "battlegrounds", "helldivers", "super earth", "baldur", "faerun", "red dead",
    "hollow knight", "silksong", "pharloom", "black myth", "wukong", "sea of thieves",
    "lethal company", "forza", "warframe", "tenno", "osu!", "euro truck", "battlefield",
    "arc raiders", "tarkov", "palworld", "stardew", "pelican town", "terraria", "diablo",
    "world of warcraft", "azeroth", "monster hunter", "path of exile", "wraeclast", "the finals", "honkai",
    "star rail", "kharkiv", "lordran", "hyrule", "mordor", "hogwarts", "pokemon", "pokémon",
)

CENTERED_TOLERANCE = 0.06
MIN_CENTERED_PER_DEFAULT_PACK = 2

DEFAULT_PACK_SIZES = {"default.soft-gradients": 10}
DEFAULT_PACK_SIZE = 8
EXPECTED_DEFAULT_PACKS = (
    "default.matte-black", "default.clean-white", "default.warm-minimal", "default.sage-botanical",
    "default.soft-gradients", "default.misty-nature", "default.painted", "default.steel-blue-night",
    "default.cozy-lofi", "default.deep-space", "default.pastel", "default.neon-minimal",
    "default.architectural",
)

_ACROSS = re.compile(r"(\d{1,3})\s*[–-]\s*(\d{1,3})%\s+across")
_DOWN = re.compile(r"(\d{1,3})\s*[–-]\s*(\d{1,3})%\s+down")
_HEX = re.compile(r"^#[0-9A-Fa-f]{6}$")
_PROMPT_HEX = re.compile(r"#[0-9A-Fa-f]{6}")


@dataclass(frozen=True)
class Finding:
    severity: str  # "error" | "warning"
    where: str
    message: str

    def __str__(self) -> str:
        return f"{self.severity.upper():7} {self.where}: {self.message}"


def word_count(text: str) -> int:
    return len(text.split())


def forbidden_terms(pack: Pack) -> list[str]:
    """Everything that identifies the game in this pack: its title, its distinctive title words, studios, proper nouns."""
    terms = [t.lower() for t in STUDIO_NAMES + PROPER_NOUNS]
    if pack.kind == "game":
        title = pack.title.lower()
        terms.append(title)
        for token in re.split(r"[^a-z0-9!]+", title):
            if len(token) >= 5 and token not in TITLE_STOPWORDS:
                terms.append(token)
    return terms


def _mentions(text_lower: str, term: str) -> bool:
    if not term:
        return False
    pattern = r"(?<![a-z0-9])" + re.escape(term) + r"(?![a-z0-9])"
    return re.search(pattern, text_lower) is not None


def lint_wallpaper(pack: Pack, wp: Wallpaper, terms: list[str]) -> list[Finding]:
    where = f"{pack.id}/{wp.id}"
    out: list[Finding] = []

    def err(message: str) -> None:
        out.append(Finding("error", where, message))

    def warn(message: str) -> None:
        out.append(Finding("warning", where, message))

    if pack.kind == "game" and not wp.id.startswith(pack.game_id + "."):
        err(f"id must start with '{pack.game_id}.'")
    if wp.role not in ROLES:
        err(f"role '{wp.role}' is not one of {ROLES}")
    if pack.kind == "default" and wp.role != "default":
        err("default-collection wallpapers use role: default")
    if pack.kind == "game" and wp.role == "default":
        err("game wallpapers need a role of hero, minimal, mood or alt")
    if wp.tone not in TONES:
        err(f"tone '{wp.tone}' is not one of {TONES}")
    if not wp.setup_match or any(s not in SETUP_MATCHES for s in wp.setup_match):
        err(f"setupMatch must be a non-empty subset of {SETUP_MATCHES}")
    if wp.upscaler not in UPSCALERS:
        err(f"upscaler '{wp.upscaler}' is not one of {UPSCALERS}")
    if not _HEX.match(wp.accent):
        err("accent must be a #RRGGBB hex colour")
    if not (0.0 <= wp.grain <= 1.0):
        err("grain must be within 0..1")
    if not (0.1 <= wp.focal_x <= 0.9 and 0.2 <= wp.focal_y <= 0.8):
        err(f"focal ({wp.focal_x}, {wp.focal_y}) lies outside the safe area (x 0.1-0.9, y 0.2-0.8)")
    if pack.kind == "game" and wp.starter:
        err("starter is for default collections only")

    missing = [k for k in PROMPT_KINDS if k not in wp.prompts and (k != "portrait" or wp.portrait)]
    if missing:
        err(f"missing prompts: {', '.join(missing)}")
    if not wp.portrait and "portrait" in wp.prompts:
        err("portrait prompt present but portrait: false")

    landscape = wp.prompts.get("landscape", "")
    if landscape:
        words = word_count(landscape)
        if not (LANDSCAPE_WORDS[0] <= words <= LANDSCAPE_WORDS[1]):
            err(f"landscape prompt is {words} words; target {LANDSCAPE_WORDS[0]}-{LANDSCAPE_WORDS[1]}")
        flat = " ".join(landscape.split())
        if not flat.startswith(OPENING):
            err(f"landscape prompt must start with '{OPENING}'")
        if SAFE_ZONE not in flat:
            err("landscape prompt is missing the standard safe-zone sentence")
        if TECHNICAL_START not in flat or "banding-free" not in flat:
            err("landscape prompt is missing the Technical sentence")
        if EXCLUSION_START not in flat:
            err("landscape prompt is missing the exclusions sentence")
        for label in ("Style:", "Palette:", "Composition:"):
            if label not in flat:
                err(f"landscape prompt is missing '{label}'")
        hexes = _PROMPT_HEX.findall(flat)
        if not (3 <= len(set(h.upper() for h in hexes)) <= 7):
            err(f"landscape prompt lists {len(set(hexes))} distinct hex colours; use 3-5 (plus an accent)")
        across, down = _ACROSS.search(flat), _DOWN.search(flat)
        if not across or not down:
            err("composition must state the subject position as 'about A-B% across and C-D% down'")
        else:
            ax, bx = int(across.group(1)), int(across.group(2))
            ay, by = int(down.group(1)), int(down.group(2))
            if not (ax - 3 <= wp.focal_x * 100 <= bx + 3):
                err(f"focal x={wp.focal_x} is outside the stated {ax}-{bx}% across")
            if not (ay - 3 <= wp.focal_y * 100 <= by + 3):
                err(f"focal y={wp.focal_y} is outside the stated {ay}-{by}% down")

    ultrawide = " ".join(wp.prompts.get("ultrawide", "").split())
    if ultrawide:
        if not ultrawide.startswith(ULTRAWIDE_START):
            err("ultrawide prompt must start with the standard 'Extend this exact image' sentence")
        for label in ("New area on the left:", "New area on the right:"):
            if label not in ultrawide:
                err(f"ultrawide prompt is missing '{label}'")
        if "No seams" not in ultrawide:
            err("ultrawide prompt must forbid seams")
        left = ultrawide.split("New area on the left:")[-1].split("New area on the right:")[0]
        if word_count(left) < 8:
            err("ultrawide prompt must describe concrete content for the new left area")

    portrait = " ".join(wp.prompts.get("portrait", "").split())
    if portrait:
        if not portrait.startswith(PORTRAIT_START):
            err("portrait prompt must start with the standard 'Using the attached image' sentence")
        if "Keep the bottom 10% calm" not in portrait:
            err("portrait prompt must keep the bottom 10% calm")

    for kind, text in wp.prompts.items():
        lower = " ".join(text.lower().split())
        for phrase in BANNED_PHRASES:
            if _mentions(lower, phrase):
                err(f"{kind} prompt uses banned phrase '{phrase}'")
        for term in terms:
            if _mentions(lower, term):
                err(f"{kind} prompt contains the proper noun '{term}' (ART_DIRECTION.md section 5)")
        if "{" in text or "}" in text or "TODO" in text or "..." in text:
            err(f"{kind} prompt contains a placeholder or ellipsis")

    return out


def lint_pack(pack: Pack) -> list[Finding]:
    out: list[Finding] = []
    where = pack.id

    def err(message: str) -> None:
        out.append(Finding("error", where, message))

    def warn(message: str) -> None:
        out.append(Finding("warning", where, message))

    if pack.path.stem != pack.id:
        err(f"file name '{pack.path.name}' must be '{pack.id}.yaml'")
    if pack.kind not in ("game", "default"):
        err("kind must be 'game' or 'default'")
        return out
    if not pack.id.startswith(pack.kind + "."):
        err(f"pack id must start with '{pack.kind}.'")
    if not pack.style_bible:
        err("styleBible is required")
    elif word_count(pack.style_bible) < 40:
        err("styleBible is too short to steer a series (needs 40+ words)")

    ids = [w.id for w in pack.wallpapers]
    if len(ids) != len(set(ids)):
        err("duplicate wallpaper ids")

    if pack.kind == "game":
        insp = pack.inspiration
        if not isinstance(insp, dict):
            err("game packs need an 'inspiration' block")
        else:
            for key in ("genre", "setting", "palette", "motifs", "avoid"):
                if not insp.get(key):
                    err(f"inspiration.{key} is required")
            palette = insp.get("palette") or []
            if not all(isinstance(c, str) and _HEX.match(c) for c in palette):
                err("inspiration.palette must be #RRGGBB strings")
            avoid = " ".join(str(a).lower() for a in (insp.get("avoid") or []))
            for needed in ("logo", "text", "character"):
                if needed not in avoid:
                    warn(f"inspiration.avoid should mention '{needed}'")
        roles = [w.role for w in pack.wallpapers]
        for needed in ("hero", "minimal", "mood"):
            if needed not in roles:
                err(f"missing required role '{needed}'")
        if not any(w.tone == "dark" for w in pack.wallpapers):
            err("a game pack MUST contain at least one tone: dark wallpaper")
        if not any(w.tone == "light" for w in pack.wallpapers):
            warn("a game pack SHOULD contain a tone: light wallpaper")
    else:
        expected = DEFAULT_PACK_SIZES.get(pack.id, DEFAULT_PACK_SIZE)
        if len(pack.wallpapers) != expected:
            err(f"default collection must have {expected} wallpapers, found {len(pack.wallpapers)}")
        starters = [w.id for w in pack.wallpapers if w.starter]
        if len(starters) != 1:
            err(f"exactly one wallpaper must be starter: true (found {len(starters)})")
        centered = [w for w in pack.wallpapers if abs(w.focal_x - 0.5) <= CENTERED_TOLERANCE]
        if len(centered) < MIN_CENTERED_PER_DEFAULT_PACK:
            err(f"at least {MIN_CENTERED_PER_DEFAULT_PACK} wallpapers must have a centred focal point (found {len(centered)})")
        left_heavy = [w.id for w in pack.wallpapers if w.focal_x < 0.5 - CENTERED_TOLERANCE]
        if left_heavy:
            warn(f"focal point left of centre (the icon column lives there): {', '.join(left_heavy)}")

    terms = forbidden_terms(pack)
    for wp in pack.wallpapers:
        out.extend(lint_wallpaper(pack, wp, terms))
    return out


def lint_all(packs: list[Pack]) -> list[Finding]:
    out: list[Finding] = []
    seen: dict[str, str] = {}
    for pack in packs:
        out.extend(lint_pack(pack))
        for wp in pack.wallpapers:
            if wp.id in seen:
                out.append(Finding("error", f"{pack.id}/{wp.id}", f"wallpaper id also used in {seen[wp.id]}"))
            seen[wp.id] = pack.id
    ids = {p.id for p in packs}
    for expected in EXPECTED_DEFAULT_PACKS:
        if packs and any(p.kind == "default" for p in packs) and expected not in ids:
            out.append(Finding("error", expected, "default collection file is missing"))
    return out
