"""``assetpipe starter``: assemble the bundled starter set that ships inside the installer (SPEC FR-CON-1).

The installer carries the catalog snapshot (``content/catalog.src.json``, copied by the App csproj) plus ``content/starter/``:
the ``16x9`` variant of every default collection's ``starter: true`` wallpaper, and the 640x360 thumbnail of every built wallpaper,
so the app is fully usable offline. Files are looked up by base name (``ContentLibrary.LocateFile``), so they are copied flat.
"""

from __future__ import annotations

import shutil
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
            if pack.kind == "default" and wp.starter:
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

    if target.exists():
        shutil.rmtree(target)
    target.mkdir(parents=True)
    for pack_id, name in wanted:
        source = layout.out_dir(pack_id) / name
        if not source.exists():
            raise StarterError(f"{source} is missing; rebuild {pack_id}")
        shutil.copy2(source, target / name)
        report.total_bytes += source.stat().st_size
        (report.variants if name.endswith("_16x9.jpg") else report.thumbs).append(name)

    if report.total_bytes > BUDGET_BYTES:
        raise StarterError(f"starter set is {report.total_bytes / 1_048_576:.1f} MB; the installer budget is 60 MB")
    return report
