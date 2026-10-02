"""Record actual generated, reviewed, and built coverage without marking unfinished work complete."""
import json
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parents[1]
queue = json.loads((root / "art/generation-queue.json").read_text(encoding="utf-8"))
catalog = json.loads((root / "content/catalog.src.json").read_text(encoding="utf-8"))
required = {"L": len(queue), "U": len(queue), "P": sum(bool(w.get("portrait")) for w in queue)}
present = {suffix: sum((root / "art/raw" / w["pack"] / f"{w['id']}_{suffix}.png").is_file() for w in queue)
           for suffix in required}
built = sum(len(json.loads(path.read_text(encoding="utf-8"))["wallpapers"])
            for path in (root / "art/out").glob("*/manifest.json"))
report = {"updatedUtc": datetime.now(timezone.utc).isoformat(), "status": "IN_PROGRESS",
          "wallpapersRequired": len(queue), "mastersRequired": required, "mastersGenerated": present,
          "wallpapersBuilt": built, "wallpapersInCatalog": sum(len(p["wallpapers"]) for p in catalog["packs"]),
          "starterCollectionsBundled": sum(any(w.get("starter") and
              (root / "content/starter" / Path(w["variants"]["16x9"]["path"]).name).is_file()
              for p in catalog["packs"] if p["id"] == c["packId"] for w in p["wallpapers"])
              for c in catalog["collections"]),
          "remaining": {k: required[k] - present[k] for k in required},
          "note": "Generation is not complete until every required master is generated, visually reviewed, built and cataloged."}
(root / "art/GENERATION_STATUS.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))
