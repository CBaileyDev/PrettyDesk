"""status / validate / build: the raw-to-variants half of the pipeline."""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass, field
from pathlib import Path

from . import imaging
from .packs import RAW_SUFFIX, Pack, Wallpaper
from .paths import Layout
from .variants import MAX_FILE_BYTES, VARIANTS, Variant


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


# ---- status -----------------------------------------------------------------------------------------------------------


@dataclass
class WallpaperStatus:
    pack: str
    wallpaper: str
    have: dict[str, bool]
    needed: list[str]
    built: bool
    approved: bool

    @property
    def complete(self) -> bool:
        return all(self.have[k] for k in self.needed)


def needed_masters(wp: Wallpaper) -> list[str]:
    return ["L", "U", "P"] if wp.portrait else ["L", "U"]


def collect_status(layout: Layout, packs: list[Pack]) -> list[WallpaperStatus]:
    rows = []
    for pack in packs:
        for wp in pack.wallpapers:
            have = {s: layout.raw_file(pack.id, wp.id, s).exists() for s in ("L", "U", "P")}
            built = (layout.out_dir(pack.id) / f"{wp.id}_16x9.jpg").exists()
            rows.append(WallpaperStatus(pack.id, wp.id, have, needed_masters(wp), built, wp.approved))
    return rows


def render_status(rows: list[WallpaperStatus]) -> str:
    if not rows:
        return "No wallpapers found in art/prompts/*.yaml."
    lines = [f"{'pack':28} {'wallpaper':26} L U P  built approved"]
    for r in rows:
        marks = " ".join(("●" if r.have[s] else "·") if s in r.needed else "-" for s in ("L", "U", "P"))
        lines.append(f"{r.pack:28} {r.wallpaper:26} {marks}  {'yes' if r.built else 'no':5} {'yes' if r.approved else 'no'}")
    done = sum(1 for r in rows if r.complete)
    built = sum(1 for r in rows if r.built)
    approved = sum(1 for r in rows if r.approved)
    lines.append("")
    lines.append(f"{len(rows)} wallpapers · {done} with all masters · {built} built · {approved} approved   (● present · missing, - not needed)")
    return "\n".join(lines)


# ---- validate ---------------------------------------------------------------------------------------------------------


def validate_pack(layout: Layout, pack: Pack) -> list[str]:
    """Problems with the raw files that exist. Missing files are reported by ``status``, not as errors here."""
    problems: list[str] = []
    for wp in pack.wallpapers:
        if not layout.raw_file(pack.id, wp.id, "L").exists() and any(layout.raw_file(pack.id, wp.id, s).exists() for s in "UP"):
            problems.append(f"{pack.id}/{wp.id}: has U/P masters but no L master")
        for suffix in ("L", "U", "P"):
            path = layout.raw_file(pack.id, wp.id, suffix)
            if not path.exists():
                continue
            if suffix == "P" and not wp.portrait:
                problems.append(f"{pack.id}/{wp.id}: portrait master present but portrait: false")
            try:
                img = imaging.load_master(path)
            except imaging.ImageProblem as ex:
                problems.append(f"{pack.id}/{wp.id}: {ex}")
                continue
            problems.extend(f"{pack.id}/{wp.id}_{suffix}: {p}" for p in imaging.check_master(img, suffix))
    return problems


# ---- build ------------------------------------------------------------------------------------------------------------


@dataclass
class BuiltFile:
    path: str  # relative to art/out/{pack}
    width: int
    height: int
    bytes: int
    sha256: str


@dataclass
class BuiltWallpaper:
    id: str
    variants: dict[str, BuiltFile] = field(default_factory=dict)
    thumb: BuiltFile | None = None
    raw_sha256: dict[str, str] = field(default_factory=dict)
    warnings: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)


def _record(out_dir: Path, name: str, data: bytes, size: tuple[int, int]) -> BuiltFile:
    (out_dir / name).write_bytes(data)
    return BuiltFile(name, size[0], size[1], len(data), sha256_bytes(data))


def build_wallpaper(layout: Layout, pack: Pack, wp: Wallpaper, only: set[str] | None = None) -> BuiltWallpaper:
    out_dir = layout.out_dir(pack.id)
    out_dir.mkdir(parents=True, exist_ok=True)
    result = BuiltWallpaper(wp.id)
    masters: dict[str, imaging.Image.Image] = {}
    for suffix in ("L", "U", "P"):
        path = layout.raw_file(pack.id, wp.id, suffix)
        if path.exists():
            result.raw_sha256[suffix] = sha256_file(path)
            try:
                masters[suffix] = imaging.load_master(path)
            except imaging.ImageProblem as ex:
                result.errors.append(str(ex))
    if "L" not in masters:
        result.errors.append("no L master; nothing to build")
        return result

    for spec in VARIANTS:
        if only and spec.key not in only:
            continue
        master = masters.get(spec.source)
        if master is None:
            if spec.source == "P":
                result.warnings.append("no portrait master: portrait monitors will focal-crop the 16x9 variant")
            else:
                result.warnings.append(f"no {spec.source} master: variant {spec.key} skipped")
            continue
        for problem in imaging.check_master(master, spec.source):
            result.errors.append(f"{spec.source}: {problem}")
        rendered = imaging.render_variant(master, spec, wp.focal_x, wp.focal_y, wp.upscaler, wp.grain)
        data = imaging.encode_jpeg(rendered.image)
        if rendered.soft:
            result.warnings.append(
                f"{spec.key}: master is {rendered.enlarge:.1f}x too small and realesrgan-ncnn-vulkan is not on PATH; "
                "used a plain Lanczos enlargement (will look soft)")
        built = _record(out_dir, f"{wp.id}_{spec.key}.jpg", data, rendered.image.size)
        if built.bytes > MAX_FILE_BYTES:
            result.errors.append(f"{spec.key}: {built.bytes / 1_048_576:.1f} MB exceeds the 8 MB limit")
        if rendered.image.size != (spec.width, spec.height):
            result.errors.append(f"{spec.key}: produced {rendered.image.size}, expected {(spec.width, spec.height)}")
        score, judged = imaging.banding_score(imaging.luma_of(rendered.image))
        if judged >= 3 and score > 0.25:
            result.warnings.append(f"{spec.key}: possible banding in {score:.0%} of {judged} smooth tiles; check at 100%")
        result.variants[spec.key] = built
        if spec.key == "16x9":
            thumb = imaging.make_thumbnail(rendered.image)
            result.thumb = _record(out_dir, f"{wp.id}_thumb.jpg", thumb, (640, 360))

    if result.thumb is None and result.variants:
        first = next(iter(result.variants))
        img = imaging.thumbnail_source(imaging.Image.open(out_dir / result.variants[first].path).convert("RGB"))
        result.thumb = _record(out_dir, f"{wp.id}_thumb.jpg", imaging.make_thumbnail(img), (640, 360))
    return result


def write_manifest(layout: Layout, pack: Pack, built: list[BuiltWallpaper]) -> Path:
    manifest = {
        "pack": pack.id,
        "wallpapers": {
            b.id: {
                "rawSha256": b.raw_sha256,
                "thumb": b.thumb.__dict__ if b.thumb else None,
                "variants": {k: v.__dict__ for k, v in b.variants.items()},
            }
            for b in built
            if b.variants and not b.errors
        },
    }
    path = layout.out_dir(pack.id) / "manifest.json"
    path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return path


def load_manifest(layout: Layout, pack_id: str) -> dict:
    path = layout.out_dir(pack_id) / "manifest.json"
    if not path.exists():
        return {"pack": pack_id, "wallpapers": {}}
    return json.loads(path.read_text(encoding="utf-8"))


_RAW_LINE = re.compile(r"^(\s*)rawSha256:.*$")


def update_raw_hashes(yaml_text: str, wallpaper_id: str, hashes: dict[str, str]) -> str:
    """Rewrite one wallpaper's ``rawSha256:`` line in place so the YAML keeps its comments and layout."""
    lines = yaml_text.split("\n")
    start = next((i for i, line in enumerate(lines) if re.match(rf"^\s*-\s+id:\s*{re.escape(wallpaper_id)}\s*$", line)), None)
    if start is None:
        return yaml_text
    for i in range(start + 1, len(lines)):
        if re.match(r"^\s*-\s+id:", lines[i]):
            break
        m = _RAW_LINE.match(lines[i])
        if m:
            body = ", ".join(f'{k}: "{v}"' for k, v in sorted(hashes.items()))
            lines[i] = f"{m.group(1)}rawSha256: {{{body}}}"
            break
    return "\n".join(lines)
