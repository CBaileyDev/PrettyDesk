"""Build generated landscapes incrementally. Review and approval remain a separate step."""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools/assetpipe"))
from assetpipe import commands, imaging
from assetpipe.packs import load_all
from assetpipe.paths import Layout
from assetpipe.variants import VARIANTS


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--starters-only", action="store_true")
    parser.add_argument("--all-landscape-variants", action="store_true")
    parser.add_argument("--all-variants", action="store_true")
    parser.add_argument("--pack", help="Build only this pack")
    args = parser.parse_args()
    exe_dir = ROOT / "tools/bin/realesrgan"
    os.environ["PATH"] = str(exe_dir) + os.pathsep + os.environ["PATH"]
    assert imaging.realesrgan_path(), "Install the official Real-ESRGAN Vulkan release in tools/bin/realesrgan"
    layout = Layout(ROOT)
    count = 0
    for pack in load_all(layout.prompts):
        if args.pack and pack.id != args.pack:
            continue
        manifest = commands.load_manifest(layout, pack.id)
        for wp in pack.wallpapers:
            if args.starters_only and not wp.starter:
                continue
            raw = layout.raw_file(pack.id, wp.id, "L")
            if not raw.is_file():
                continue
            previous = manifest["wallpapers"].get(wp.id, {})
            raw_hash = commands.sha256_file(raw)
            wanted = {"16x9", "16x10", "3x2"} if args.all_landscape_variants else {"16x9"}
            hashes = {"L": raw_hash}
            if args.all_variants:
                wanted = {v.key for v in VARIANTS if layout.raw_file(pack.id, wp.id, v.source).is_file()}
                hashes = {s: commands.sha256_file(layout.raw_file(pack.id, wp.id, s))
                          for s in "LUP" if layout.raw_file(pack.id, wp.id, s).is_file()}
            if all(previous.get("rawSha256", {}).get(s) == h for s, h in hashes.items()) and wanted.issubset(previous.get("variants", {})):
                continue
            print(f"Building {pack.id}/{wp.id}", flush=True)
            built = commands.build_wallpaper(layout, pack, wp, wanted)
            if built.errors:
                raise RuntimeError("; ".join(built.errors))
            entry = {
                "rawSha256": built.raw_sha256,
                "thumb": built.thumb.__dict__ if built.thumb else None,
                "variants": {k: v.__dict__ for k, v in built.variants.items()},
            }
            manifest["wallpapers"][wp.id] = entry
            out = layout.out_dir(pack.id)
            out.mkdir(parents=True, exist_ok=True)
            (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            text = pack.path.read_text(encoding="utf-8")
            pack.path.write_text(commands.update_raw_hashes(text, wp.id, built.raw_sha256), encoding="utf-8")
            count += 1
            for warning in built.warnings:
                print(warning, flush=True)
    print(f"Built {count} landscapes", flush=True)


if __name__ == "__main__":
    main()
