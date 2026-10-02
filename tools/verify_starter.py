"""Verify the offline bundle against the exact catalog hashes and required starter entries."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
catalog = json.loads((ROOT / "content/catalog.src.json").read_text(encoding="utf-8"))
starter = ROOT / "content/starter"
policy_file = ROOT / "content/offline-bundle.json"
policy = json.loads(policy_file.read_text()) if policy_file.exists() else {}
game_packs = set(policy.get("gamePacks", []))
required = []
for pack in catalog["packs"]:
    for wallpaper in pack["wallpapers"]:
        if wallpaper.get("thumb"):
            required.append(wallpaper["thumb"])
        if "bundled" in wallpaper.get("tags", []):
            required.extend(wallpaper["variants"].values())
        elif wallpaper.get("starter") or pack["id"] in game_packs:
            required.append(wallpaper["variants"]["16x9"])
for collection in catalog["collections"]:
    if collection["id"] == "user":
        continue
    pack = next(p for p in catalog["packs"] if p["id"] == collection["packId"])
    assert any(w.get("starter") for w in pack["wallpapers"]), f"Missing starter: {pack['id']}"
assert required, "Empty starter bundle"
for ref in required:
    path = starter / Path(ref["path"]).name
    assert path.is_file(), f"Missing {path.name}"
    assert hashlib.sha256(path.read_bytes()).hexdigest() == ref["sha256"], f"Hash mismatch: {path.name}"
total = sum(p.stat().st_size for p in starter.iterdir() if p.is_file())
assert total <= policy.get("maxBytes", 60 * 1024 * 1024), "Starter exceeds offline bundle policy"
print(f"Verified {len(required)} catalog assets; bundle {total / 1048576:.2f} MB")
