"""``assetpipe publish``: approved + built wallpapers -> catalog.src.json -> catalog.json -> signed -> uploaded (SPEC 6.2, 8)."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path

from .commands import load_manifest, sha256_file
from .packs import Pack, Wallpaper
from .paths import Layout


class PublishError(Exception):
    pass


def _file_ref(rel_dir: str, built: dict) -> dict:
    return {"path": f"{rel_dir}/{built['path']}", "w": built["width"], "h": built["height"], "bytes": built["bytes"], "sha256": built["sha256"]}


def wallpaper_entry(wp: Wallpaper, built: dict, rel_dir: str) -> dict:
    entry = {
        "id": wp.id,
        "title": wp.title,
        "role": wp.role,
        "tone": wp.tone,
        "tags": [*wp.tags, *([wp.role] if wp.role not in ("default", *wp.tags) else [])],
        "setupMatch": wp.setup_match,
        "focal": {"x": wp.focal_x, "y": wp.focal_y},
        "accent": wp.accent,
        "starter": wp.starter,
        "thumb": _file_ref(rel_dir, built["thumb"]) if built.get("thumb") else None,
        "variants": {key: _file_ref(rel_dir, v) for key, v in built["variants"].items()},
    }
    return {k: v for k, v in entry.items() if v is not None}


def check_publishable(layout: Layout, pack: Pack) -> tuple[dict, list[str]]:
    manifest = load_manifest(layout, pack.id)
    problems: list[str] = []
    for wp in pack.wallpapers:
        if not wp.approved:
            problems.append(f"{wp.id}: not approved (set approved: true after review)")
            continue
        built = manifest["wallpapers"].get(wp.id)
        if not built or "16x9" not in built["variants"]:
            problems.append(f"{wp.id}: not built (run 'assetpipe build --pack {pack.id}')")
            continue
        for key, ref in built["variants"].items():
            path = layout.out_dir(pack.id) / ref["path"]
            if not path.exists():
                problems.append(f"{wp.id}: {ref['path']} is missing from art/out")
            elif sha256_file(path) != ref["sha256"]:
                problems.append(f"{wp.id}: {ref['path']} changed since the build (rebuild)")
    return manifest, problems


def update_catalog_source(layout: Layout, pack: Pack, manifest: dict, today: datetime | None = None) -> dict:
    """Return the updated catalog.src.json document with ``pack`` filled in from the approved, built wallpapers."""
    src = json.loads(layout.catalog_src.read_text(encoding="utf-8"))
    pack_entry = next((p for p in src["packs"] if p["id"] == pack.id), None)
    if pack_entry is None:
        raise PublishError(f"catalog.src.json has no pack '{pack.id}'; add it (and its game or collection) first")
    version = int(pack_entry.get("version", 0)) + 1
    rel_dir = f"packs/{pack.id}/v{version}"
    wallpapers = [wallpaper_entry(wp, manifest["wallpapers"][wp.id], rel_dir) for wp in pack.wallpapers if wp.approved]
    pack_entry["version"] = version
    pack_entry["wallpapers"] = wallpapers
    stamp = (today or datetime.now(timezone.utc)).strftime("%Y.%m.%d")
    current = src.get("catalogVersion", "")
    seq = int(current.rsplit(".", 1)[1]) + 1 if current.startswith(stamp) and "." in current[len(stamp):] else 1
    src["catalogVersion"] = f"{stamp}.{seq}"
    return src


def stage_upload(layout: Layout, pack: Pack, pack_entry: dict, version: int) -> Path:
    """Copy exactly the files the catalog references into art/out/publish/ in their final URL layout."""
    target = layout.publish_dir
    dest = target / "packs" / pack.id / f"v{version}"
    dest.mkdir(parents=True, exist_ok=True)
    for wp in pack_entry["wallpapers"]:
        refs = [wp["thumb"], *wp["variants"].values()] if wp.get("thumb") else list(wp["variants"].values())
        for ref in refs:
            shutil.copy2(layout.out_dir(pack.id) / Path(ref["path"]).name, dest / Path(ref["path"]).name)
    return target


def sign_catalog(layout: Layout, catalog_json: Path, key_env: str = "PRETTYDESK_CATALOG_KEY") -> None:
    if not os.environ.get(key_env):
        raise PublishError(f"set {key_env} to the base64 private key (never commit it). Use 'catalog-sign keygen' once, offline.")
    project = layout.root / "tools" / "catalog-sign"
    result = subprocess.run(
        ["dotnet", "run", "--project", str(project), "-c", "Release", "--", "sign", str(catalog_json), "--key-env", key_env],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        raise PublishError(f"catalog-sign failed: {result.stderr.strip() or result.stdout.strip()}")


def upload(layout: Layout, command_template: str | None) -> None:
    """Run the owner's upload command, e.g. ``rclone copy {src} r2:prettydesk/v1/``. ``{src}`` is art/out/publish."""
    if not command_template:
        raise PublishError(
            "no upload command configured. Set PRETTYDESK_UPLOAD_CMD, e.g. 'rclone copy {src} r2:prettydesk-content/v1/' "
            "or 'aws s3 sync {src} s3://bucket/v1/ --endpoint-url ...'. Upload variants before the catalog."
        )
    command = command_template.replace("{src}", str(layout.publish_dir))
    if subprocess.run(command, shell=True).returncode != 0:
        raise PublishError("upload command failed")
