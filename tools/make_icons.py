#!/usr/bin/env python3
"""Generates the placeholder app icon and the light/dark tray icons (OWNER-DECISION: replace with the final brand icon).

  app icon : rounded square, deep-indigo → teal gradient, a white monitor with a warm "moon" dot.
  tray     : monochrome monitor glyph, dark for light taskbars and white for dark taskbars.
"""
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src" / "PrettyDesk.App" / "Assets"
SS = 8  # supersampling factor for crisp edges


def gradient(size, top, bottom):
    img = Image.new("RGBA", (size, size))
    px = img.load()
    for y in range(size):
        t = y / (size - 1)
        color = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        for x in range(size):
            px[x, y] = color
    return img


def monitor(draw, size, color, accent=None, stroke=0.07):
    s = size
    w = max(1, round(s * stroke))
    left, top, right, bottom = s * 0.2, s * 0.24, s * 0.8, s * 0.64
    draw.rounded_rectangle([left, top, right, bottom], radius=s * 0.06, outline=color, width=w)
    cx = s / 2
    draw.rectangle([cx - w / 2, bottom, cx + w / 2, s * 0.74], fill=color)
    draw.rounded_rectangle([cx - s * 0.14, s * 0.74, cx + s * 0.14, s * 0.74 + w], radius=w / 2, fill=color)
    if accent:
        r = s * 0.065
        draw.ellipse([right - s * 0.2 - r, top + s * 0.1 - r, right - s * 0.2 + r, top + s * 0.1 + r], fill=accent)


def app_icon(size):
    big = size * SS
    base = gradient(big, (42, 47, 85), (63, 182, 168))
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, big - 1, big - 1], radius=big * 0.22, fill=255)
    base.putalpha(mask)
    draw = ImageDraw.Draw(base)
    monitor(draw, big, (255, 255, 255, 255), accent=(255, 183, 77, 255), stroke=0.075)
    return base.resize((size, size), Image.LANCZOS)


def tray_icon(size, color):
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    monitor(ImageDraw.Draw(img), big, color + (255,), accent=color + (255,), stroke=0.1)
    return img.resize((size, size), Image.LANCZOS)


def save_ico(path, factory, sizes):
    images = [factory(s) for s in sizes]
    images[-1].save(path, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    save_ico(OUT / "PrettyDesk.ico", app_icon, [16, 24, 32, 48, 64, 128, 256])
    save_ico(OUT / "tray-for-light-taskbar.ico", lambda s: tray_icon(s, (32, 32, 32)), [16, 20, 24, 32, 48])
    save_ico(OUT / "tray-for-dark-taskbar.ico", lambda s: tray_icon(s, (255, 255, 255)), [16, 20, 24, 32, 48])
    print("icons written to", OUT)
