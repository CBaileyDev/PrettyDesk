"""``assetpipe starter``: assemble the bundled starter set that ships inside the installer (SPEC FR-CON-1).

The installer carries the catalog snapshot (``content/catalog.src.json``, copied by the App csproj) plus ``content/starter/``:
the ``16x9`` variant of every default collection's ``starter: true`` wallpaper, and the 640x360 thumbnail of every built wallpaper,
so the app is fully usable offline. Default wallpapers tagged ``bundled`` additionally ship all built ratios.
Files are looked up by base name (``ContentLibrary.LocateFile``), so they are copied flat.
"""

from __future__ import annotations

import shutil
import json
import hashlib
import tempfile
import os
from dataclasses import dataclass, field

from .commands import load_manifest
from .packs import Pack
from .paths import Layout

BUDGET_BYTES = 60 * 1024 * 1024  # SPEC FR-CON-1: 60 MB of content or less


class StarterError(Exception):
    pass


@dataclass
class StarterReport:
    variants: list[str] = field(default_factory=list)
    thumbs: list[str] = field(default_factory=list)
    missing_starters: list[str] = field(default_factory=list)
    total_bytes: int = 0


def build_starter(layout: Layout, packs: list[Pack]) -> StarterReport:
    target = layout.root / "content" / "starter"
    report = StarterReport()
    policy_file = layout.root / "content" / "offline-bundle.json"
    policy = json.loads(policy_file.read_text(encoding="utf-8")) if policy_file.exists() else {}
    budget = int(policy.get("maxBytes", BUDGET_BYTES))
    game_packs = set(policy.get("gamePacks", []))
    unknown = game_packs - {p.id for p in packs if p.kind == "game"}
    if unknown:
        raise StarterError(f"unknown offline game packs: {sorted(unknown)}")
    wanted: list[tuple[str, str]] = []  # (pack id, file name)

    for pack in packs:
        manifest = load_manifest(layout, pack.id)["wallpapers"]
        for wp in pack.wallpapers:
            entry = manifest.get(wp.id)
            if entry is None or not wp.approved:
                if pack.kind == "default" and wp.starter:
                    report.missing_starters.append(f"{pack.id}/{wp.id}")
                continue
            if entry.get("thumb"):
                wanted.append((pack.id, entry["thumb"]["path"]))
            if pack.kind == "default" and "bundled" in wp.tags:
                wanted.extend((pack.id, ref["path"]) for ref in entry["variants"].values())
            elif (pack.kind == "default" and wp.starter) or pack.id in game_packs:
                ref = entry["variants"].get("16x9")
                if ref is None:
                    report.missing_starters.append(f"{pack.id}/{wp.id} (no 16x9 variant)")
                else:
                    wanted.append((pack.id, ref["path"]))

    if report.missing_starters:
        raise StarterError(
            "starter wallpapers that are not approved and built yet: " + ", ".join(report.missing_starters)
            + ". Build, review and approve them first."
        )

    # Validate the complete plan before touching an existing bundle. Preserve unrelated outputs.
    sources = {}
    for pack_id, name in wanted:
        source = layout.out_dir(pack_id) / name
        if not source.exists():
            raise StarterError(f"{source} is missing; rebuild {pack_id}")
        manifest = load_manifest(layout, pack_id)["wallpapers"]
        refs = [ref for wp in manifest.values() for ref in [wp.get("thumb"), *wp.get("variants", {}).values()] if ref]
        ref = next((r for r in refs if r["path"] == name), None)
        if ref is None or hashlib.sha256(source.read_bytes()).hexdigest() != ref["sha256"]:
            raise StarterError(f"{name} changed since its build; rebuild and review before bundling")
        if name in sources and sources[name] != source:
            raise StarterError(f"duplicate offline asset name: {name}")
        sources[name] = source
    retained = sum(p.stat().st_size for p in target.iterdir() if p.is_file() and p.name not in sources) if target.exists() else 0
    report.total_bytes = retained + sum(p.stat().st_size for p in sources.values())
    if report.total_bytes > budget:
        raise StarterError(f"starter set is {report.total_bytes / 1_048_576:.1f} MB; budget is {budget / 1_048_576:g} MB")
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="starter-stage-", dir=target.parent) as staging:
        for name, source in sources.items():
            shutil.copy2(source, os.path.join(staging, name))
        target.mkdir(parents=True, exist_ok=True)
        for name in sources:
            os.replace(os.path.join(staging, name), target / name)
            (report.thumbs if name.endswith("_thumb.jpg") else report.variants).append(name)
    return report
