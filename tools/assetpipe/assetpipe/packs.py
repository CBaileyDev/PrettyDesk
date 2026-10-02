"""Loading ``art/prompts/{packId}.yaml`` (ART_DIRECTION.md section 7)."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml

ROLES = ("hero", "minimal", "mood", "alt", "default")
TONES = ("dark", "light", "mid")
SETUP_MATCHES = ("black", "white", "wood", "pastel", "rgb")
UPSCALERS = ("x4plus", "x4plus-anime")
PROMPT_KINDS = ("landscape", "ultrawide", "portrait")
RAW_SUFFIX = {"landscape": "L", "ultrawide": "U", "portrait": "P"}


class PackError(Exception):
    """A pack file that cannot even be parsed into the model (as opposed to lint findings)."""


@dataclass
class Wallpaper:
    id: str
    title: str
    role: str
    tone: str
    setup_match: list[str]
    tags: list[str]
    starter: bool
    accent: str
    focal_x: float
    focal_y: float
    upscaler: str
    grain: float
    portrait: bool
    prompts: dict[str, str]
    review_notes: str = ""
    approved: bool = False
    raw_sha256: dict[str, str] = field(default_factory=dict)


@dataclass
class Pack:
    id: str
    kind: str
    title: str
    style_bible: str
    wallpapers: list[Wallpaper]
    inspiration: dict[str, Any] | None
    path: Path
    raw: dict[str, Any]
    art_mode: str = "generic"
    named_subjects: list[str] = field(default_factory=list)

    @property
    def game_id(self) -> str:
        return self.id.split(".", 1)[1] if self.kind == "game" else ""


def _as_list(value: Any) -> list[str]:
    if value is None:
        return []
    if isinstance(value, list):
        return [str(v) for v in value]
    raise PackError(f"expected a list, got {type(value).__name__}")


def _wallpaper(raw: dict[str, Any], kind: str) -> Wallpaper:
    try:
        focal = raw.get("focal") or {}
        return Wallpaper(
            id=str(raw["id"]),
            title=str(raw["title"]),
            role=str(raw.get("role", "default")),
            tone=str(raw["tone"]),
            setup_match=_as_list(raw.get("setupMatch")),
            tags=_as_list(raw.get("tags")),
            starter=bool(raw.get("starter", False)),
            accent=str(raw.get("accent", "")),
            focal_x=float(focal["x"]),
            focal_y=float(focal["y"]),
            upscaler=str(raw.get("upscaler", "x4plus")),
            grain=float(raw.get("grain", 0.15)),
            portrait=bool(raw.get("portrait", False)),
            prompts={k: str(v).strip() for k, v in (raw.get("prompts") or {}).items()},
            review_notes=str(raw.get("reviewNotes", "") or ""),
            approved=bool(raw.get("approved", False)),
            raw_sha256={str(k): str(v) for k, v in (raw.get("rawSha256") or {}).items()},
        )
    except KeyError as ex:
        raise PackError(f"wallpaper '{raw.get('id', '?')}' is missing field {ex}") from ex


def load_pack(path: Path) -> Pack:
    try:
        raw = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as ex:
        raise PackError(f"{path.name}: invalid YAML: {ex}") from ex
    if not isinstance(raw, dict):
        raise PackError(f"{path.name}: top level must be a mapping")
    for key in ("pack", "kind", "title", "wallpapers"):
        if key not in raw:
            raise PackError(f"{path.name}: missing '{key}'")
    kind = str(raw["kind"])
    return Pack(
        id=str(raw["pack"]),
        kind=kind,
        title=str(raw["title"]),
        style_bible=str(raw.get("styleBible", "") or "").strip(),
        wallpapers=[_wallpaper(w, kind) for w in raw["wallpapers"]],
        inspiration=raw.get("inspiration"),
        path=path,
        raw=raw,
        art_mode=str(raw.get("artMode", "generic")),
        named_subjects=_as_list(raw.get("namedSubjects")),
    )


def load_all(prompts_dir: Path) -> list[Pack]:
    packs = [load_pack(p) for p in sorted(prompts_dir.glob("*.yaml"))]
    return sorted(packs, key=lambda p: (p.kind != "default", p.id))


def find_pack(packs: list[Pack], pack_id: str) -> Pack:
    for pack in packs:
        if pack.id == pack_id:
            return pack
    known = ", ".join(p.id for p in packs) or "(none)"
    raise PackError(f"unknown pack '{pack_id}'. Known packs: {known}")
