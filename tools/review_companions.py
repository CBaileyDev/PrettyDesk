"""Create labeled sheets for reviewing generated ultrawide and portrait masters."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps

root = Path(__file__).resolve().parents[1]
queue = json.loads((root / "art/generation-queue.json").read_text(encoding="utf-8"))
found = [(w, suffix) for w in queue for suffix in "UP"
         if (root / "art/raw" / w["pack"] / f"{w['id']}_{suffix}.png").is_file()]
review = root / "art/review"
review.mkdir(parents=True, exist_ok=True)
for offset in range(0, len(found), 28):
    canvas = Image.new("RGB", (1200, 7 * 204), "#202225")
    draw = ImageDraw.Draw(canvas)
    for i, (wallpaper, suffix) in enumerate(found[offset:offset + 28]):
        x, y = i % 4 * 300, i // 4 * 204
        with Image.open(root / "art/raw" / wallpaper["pack"] / f"{wallpaper['id']}_{suffix}.png") as raw:
            preview = ImageOps.contain(raw, (292, 164))
            canvas.paste(preview, (x + (292 - preview.width) // 2, y + (164 - preview.height) // 2))
        draw.text((x + 4, y + 166), f"{wallpaper['pack']} / {wallpaper['id']}_{suffix}", fill="white")
        draw.text((x + 4, y + 180), wallpaper["title"], fill="#c6c6c6")
    path = review / f"companions-{offset // 28 + 1:02}.png"
    canvas.save(path)
    print(path)
(review / "candidate-companions.json").write_text(json.dumps([f"{w['id']}_{s}" for w, s in found], indent=2), encoding="utf-8")
print(f"{len(found)} generated companions. Inspect sheets before approving.")
