"""Create labeled, paged contact sheets and a list for the subsequent review step."""
import json
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
queue = json.loads((root / "art/generation-queue.json").read_text(encoding="utf-8"))
found = [w for w in queue if (root / "art/raw" / w["pack"] / (w["id"] + "_L.png")).exists()]
review = root / "art/review"
review.mkdir(parents=True, exist_ok=True)
for offset in range(0, len(found), 28):
    batch = found[offset:offset + 28]
    canvas = Image.new("RGB", (1200, 7 * 204), "#202225")
    draw = ImageDraw.Draw(canvas)
    for i, wallpaper in enumerate(batch):
        x, y = i % 4 * 300, i // 4 * 204
        with Image.open(root / "art/raw" / wallpaper["pack"] / (wallpaper["id"] + "_L.png")) as raw:
            canvas.paste(raw.resize((292, 164)), (x, y))
        draw.text((x + 4, y + 166), wallpaper["pack"] + " / " + wallpaper["id"], fill="white")
        draw.text((x + 4, y + 180), wallpaper["title"], fill="#c6c6c6")
    path = review / f"generated-{offset // 28 + 1:02}.png"
    canvas.save(path)
    print(path)
(review / "candidate-ids.json").write_text(json.dumps([w["id"] for w in found], indent=2), encoding="utf-8")
print(f"{len(found)} generated landscapes. Visually review sheets before copying candidate-ids.json to reviewed-ids.json.")
