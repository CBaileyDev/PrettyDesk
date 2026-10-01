"""``assetpipe review``: a static HTML contact sheet with the safe-zone overlays the owner approves against (SPEC section 8)."""

from __future__ import annotations

import html
from pathlib import Path

from .packs import Pack
from .paths import Layout
from .variants import VARIANTS

ICON_COLUMN = 0.12      # left 12% of the width
TASKBAR_1080 = 48 / 1080  # bottom 48 px at 1080p scale

CSS = """
:root{color-scheme:dark light;--bg:#0f1012;--fg:#e8e8ea;--mute:#9a9aa2;--card:#17181b;--line:#2a2b30}
@media (prefers-color-scheme:light){:root{--bg:#f4f4f6;--fg:#18181b;--mute:#62626a;--card:#fff;--line:#dcdce0}}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,Segoe UI,sans-serif;padding:24px 16px 64px}
main{max-width:1400px;margin:0 auto}h1{font-size:22px;margin:0 0 4px}.sub{color:var(--mute);margin:0 0 24px}
article{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:16px;margin:0 0 20px}
h2{font-size:16px;margin:0 0 4px}.meta{color:var(--mute);margin:0 0 12px;font-size:12px}
.row{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:12px}
figure{margin:0}figcaption{font-size:12px;color:var(--mute);margin:4px 0 0}
.frame{position:relative;overflow:hidden;border-radius:6px;background:#000}.frame img{display:block;width:100%;height:auto}
.icons{position:absolute;left:0;top:0;bottom:0;background:rgba(255,64,64,.22);outline:1px dashed rgba(255,64,64,.9)}
.task{position:absolute;left:0;right:0;bottom:0;background:rgba(64,128,255,.25);outline:1px dashed rgba(64,128,255,.9)}
.dot{position:absolute;width:14px;height:14px;margin:-7px 0 0 -7px;border:2px solid #fff;border-radius:50%;box-shadow:0 0 0 2px #000;background:rgba(255,220,0,.7)}
.zoom{width:100%;height:240px;background-repeat:no-repeat;border-radius:6px;background-color:#000}
.flag{color:#e5a000}.ok{color:#3fb27f}label{display:block;margin:6px 0 2px;font-size:12px;color:var(--mute)}
"""


def _overlay(variant_ratio: float, fx: float, fy: float) -> str:
    return (
        f'<div class="icons" style="width:{ICON_COLUMN * 100:.1f}%"></div>'
        f'<div class="task" style="height:{TASKBAR_1080 * 100:.2f}%"></div>'
        f'<div class="dot" style="left:{fx * 100:.1f}%;top:{fy * 100:.1f}%"></div>'
    )


def render_pack(layout: Layout, pack: Pack, manifest: dict) -> str:
    base = Path("..") / ".." / "out" / pack.id
    cards = []
    for wp in pack.wallpapers:
        entry = manifest.get("wallpapers", {}).get(wp.id)
        if not entry:
            cards.append(f'<article><h2>{html.escape(wp.title)} <small>({html.escape(wp.id)})</small></h2><p class="meta flag">Not built yet</p></article>')
            continue
        figures = []
        for spec in VARIANTS:
            v = entry["variants"].get(spec.key)
            if not v:
                continue
            src = html.escape((base / v["path"]).as_posix())
            zoom_x = wp.focal_x * 100
            zoom_y = wp.focal_y * 100
            figures.append(
                f'<figure><div class="frame"><img loading="lazy" src="{src}" alt="{html.escape(wp.title)} {spec.key}">{_overlay(spec.ratio, wp.focal_x, wp.focal_y)}</div>'
                f'<figcaption>{spec.key} · {v["width"]}×{v["height"]} · {v["bytes"] / 1_048_576:.1f} MB</figcaption>'
                f'<label>100% crop at the focal point</label>'
                f'<div class="zoom" style="background-image:url(\'{src}\');background-position:{zoom_x:.1f}% {zoom_y:.1f}%"></div></figure>'
            )
        status = '<span class="ok">approved</span>' if wp.approved else '<span class="flag">awaiting approval (set approved: true in the YAML)</span>'
        cards.append(
            f'<article><h2>{html.escape(wp.title)} <small>({html.escape(wp.id)})</small></h2>'
            f'<p class="meta">{html.escape(wp.role)} · {html.escape(wp.tone)} · focal {wp.focal_x:.2f}, {wp.focal_y:.2f} · {status}</p>'
            f'<div class="row">{"".join(figures)}</div></article>'
        )
    return "\n".join(cards)


def write_review(layout: Layout, pack: Pack, manifest: dict) -> Path:
    out_dir = layout.review / pack.id
    out_dir.mkdir(parents=True, exist_ok=True)
    body = render_pack(layout, pack, manifest)
    page = (
        '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
        f"<title>Review: {html.escape(pack.title)}</title><style>{CSS}</style></head><body><main>"
        f"<h1>{html.escape(pack.title)}</h1>"
        '<p class="sub">Red = desktop-icon column (left 12%). Blue = taskbar. Yellow dot = focal point. '
        "Check each against the ART_DIRECTION.md review checklist, then set <code>approved: true</code>.</p>"
        f"{body}</main></body></html>"
    )
    path = out_dir / "index.html"
    path.write_text(page, encoding="utf-8")
    return path
