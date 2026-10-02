"""Assemble reviewed, built art into the offline catalog and host staging directory."""
import argparse
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools/assetpipe"))
from assetpipe import commands, publish, starter
from assetpipe.packs import load_all
from assetpipe.paths import Layout

parser = argparse.ArgumentParser()
parser.add_argument("--reviewed", type=Path, required=True, help="JSON list of reviewed wallpaper IDs")
args = parser.parse_args()
reviewed = set(json.loads(args.reviewed.read_text(encoding="utf-8")))
layout = Layout(ROOT)
catalog = json.loads(layout.catalog_src.read_text(encoding="utf-8"))
staged = layout.publish_dir
staged.mkdir(parents=True, exist_ok=True)
total = 0
for pack in load_all(layout.prompts):
    manifest = commands.load_manifest(layout, pack.id)
    entry = next(p for p in catalog["packs"] if p["id"] == pack.id)
    existing = {w["id"]: w for w in entry["wallpapers"]}
    for wp in pack.wallpapers:
        built = manifest["wallpapers"].get(wp.id)
        if not built or wp.id not in reviewed:
            continue
        relative = f"packs/{pack.id}/v{entry['version']}"
        existing[wp.id] = publish.wallpaper_entry(wp, built, relative)
        text = pack.path.read_text(encoding="utf-8")
        # Keep author formatting and prompts intact, only update the reviewed entry.
        import re
        pattern = r"(  - id: " + re.escape(wp.id) + r"\n)(.*?)(?=\n  - id: |\Z)"
        text = re.sub(pattern, lambda m: m[1] + m[2].replace("approved: false", "approved: true")
                      .replace('reviewNotes: ""', 'reviewNotes: "Generated with built-in imagegen; visually reviewed in contact sheet by Codex on 2026-10-01."'), text, flags=re.S)
        pack.path.write_text(text, encoding="utf-8")
        total += 1
    entry["wallpapers"] = list(existing.values())
    for wp in entry["wallpapers"]:
        for ref in [wp["thumb"], *wp["variants"].values()]:
            source = layout.out_dir(pack.id) / Path(ref["path"]).name
            destination = staged / ref["path"]
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
catalog["catalogVersion"] = "2026.10.01." + str(int(catalog["catalogVersion"].split(".")[-1]) + 1)
layout.catalog_src.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
(staged / "catalog.json").write_bytes(layout.catalog_src.read_bytes())
report = starter.build_starter(layout, load_all(layout.prompts))
print(f"Assembled {total} reviewed wallpapers; starter bundle {report.total_bytes / 1048576:.2f} MB")
