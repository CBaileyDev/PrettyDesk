#!/usr/bin/env python3
"""Builds the README images in docs/images from the bundled starter wallpapers.

hero.jpg            wallpaper fan with the wordmark
gallery-games.jpg   six game wallpapers
gallery-defaults.jpg six default-collection wallpapers
how-it-works.png    the detect / match / apply / restore loop
ui-<page>-<theme>.jpg  design renders of Home, Library and Settings

The ui-* images are NOT screenshots of the running app. They are HTML renders that reuse the app's own resource strings
(Strings.resx), colour tokens (Palette.*.xaml values, ADR 0016) and layout, so the README can show the dashboard design
without a Windows machine. Replace them with real captures when a Windows capture pass is available.

Needs: Pillow, Chromium (CHROME env var, or the Playwright copy), and optionally the Inter font files
(FONTS env var: a folder with inter-latin-{400,500,600,700}-normal.woff2, from the OFL-licensed @fontsource/inter package).
Run from anywhere:  python tools/readme-assets/build.py
"""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import tempfile
from html import escape
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
STARTER = ROOT / "content" / "starter"
OUT = ROOT / "docs" / "images"
WORK = Path(tempfile.mkdtemp(prefix="prettydesk-readme-"))

CHROME = os.environ.get("CHROME") or next(
    (str(p) for p in Path("/opt/pw-browsers").glob("chromium-*/chrome-linux/chrome")),
    shutil.which("chromium") or shutil.which("google-chrome") or "chrome",
)
FONTS = Path(os.environ.get("FONTS", ""))

# ---- resources -------------------------------------------------------------------------------------------------------

_resx = (ROOT / "src/PrettyDesk.Presentation/Resources/Strings.resx").read_text(encoding="utf-8")
_strings = dict(re.findall(r'<data name="([^"]+)" xml:space="preserve">\s*<value>(.*?)</value>', _resx, flags=re.S))


def S(key: str, *args) -> str:
    """A string from Strings.resx, so the renders say what the app says."""
    text = _strings[key]
    for i, arg in enumerate(args):
        text = text.replace("{" + str(i) + "}", str(arg))
    return escape(text)


def palette(name: str) -> dict[str, str]:
    xaml = (ROOT / f"src/PrettyDesk.App/Resources/Palette.{name}.xaml").read_text(encoding="utf-8")
    return {k: v for k, v in re.findall(r'x:Key="(\w+)" Color="(#[0-9A-Fa-f]+)"', xaml)}


THEMES = {"light": palette("Light"), "dark": palette("Dark")}
CATALOG = json.loads((ROOT / "content/catalog.src.json").read_text(encoding="utf-8"))


def argb(color: str) -> str:
    """#AARRGGBB (XAML) to a CSS colour."""
    if len(color) == 9:
        a = int(color[1:3], 16) / 255
        return f"rgba({int(color[3:5], 16)},{int(color[5:7], 16)},{int(color[7:9], 16)},{a:.3f})"
    return color


# ---- images ----------------------------------------------------------------------------------------------------------

def art(name: str, width: int) -> str:
    """A starter image resized to `width`, as a file:// URL for the HTML."""
    out = WORK / f"{name}-{width}.jpg"
    if not out.exists():
        with Image.open(STARTER / f"{name}.jpg") as im:
            im.draft("RGB", (width, width))
            im = im.convert("RGB")
            im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
            im.save(out, quality=88)
    return out.as_uri()


def render(html: str, name: str, width: int, height: int, fmt: str = "jpg") -> None:
    page = WORK / f"{name}.html"
    page.write_text(html, encoding="utf-8")
    png = WORK / f"{name}.png"  # headless Chromium reserves some height for its own chrome, so over-size the window and crop below
    subprocess.run(
        [CHROME, "--headless=new", "--no-sandbox", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
         "--allow-file-access-from-files", f"--window-size={width},{height + 160}", "--virtual-time-budget=4000",
         f"--screenshot={png}", page.as_uri()],
        check=True, capture_output=True,
    )
    OUT.mkdir(parents=True, exist_ok=True)
    with Image.open(png) as im:
        im = im.convert("RGB").crop((0, 0, width, height))
        if fmt == "png":
            im.save(OUT / f"{name}.png", optimize=True)
        else:
            im.save(OUT / f"{name}.jpg", quality=90, optimize=True, progressive=True)
    print("wrote", OUT / f"{name}.{fmt}")


def font_css() -> str:
    rules = []
    for weight in (400, 500, 600, 700, 800):
        src = FONTS / f"inter-latin-{weight}-normal.woff2"
        if src.exists():
            rules.append(f"@font-face{{font-family:'Inter';font-weight:{weight};src:url('{src.as_uri()}') format('woff2');}}")
    return "".join(rules) + "body{font-family:'Inter','Segoe UI Variable','Segoe UI','Liberation Sans',sans-serif;}"


LOGO = (
    '<svg viewBox="0 0 32 28" width="{w}" height="{h}" fill="none"><rect x="1" y="1" width="28" height="19" rx="4" stroke="{ink}" stroke-width="1.8"/>'
    '<path d="M5 15 11 8 16 13 20 9 25 15Z" fill="{accent}"/>'
    '<path d="M15 21v4M9 25h12" stroke="{ink}" stroke-width="1.8" stroke-linecap="round"/></svg>'
)

# ---- hero ------------------------------------------------------------------------------------------------------------

def hero() -> None:
    cards = [
        ("rocket-league.octane-aerial_16x9", -20, 40, 0.80),
        ("minecraft.hero-01_16x9", -12, 90, 0.88),
        ("cyberpunk.hero-01_16x9", 0, 140, 1.0),
        ("fortnite.hero-01_16x9", 12, 190, 0.88),
        ("ios.tide_16x9", 20, 240, 0.80),
    ]
    fan = "".join(
        f'<div class="c" style="left:{700 + i * 150}px;top:{y}px;transform:rotate({rot / 4}deg) scale({scale});z-index:{10 - abs(i - 2)};'
        f'background-image:url({art(n, 900)})"></div>'
        for i, (n, rot, y, scale) in enumerate(cards)
    )
    logo = LOGO.format(w=64, h=56, ink="#EEF1F8", accent="#8B8DFA")
    html = f"""<!doctype html><meta charset=utf-8><style>{font_css()}
    html,body{{margin:0;width:1600px;height:640px;overflow:hidden;background:#0B0D12;color:#EEF1F8}}
    .bg{{position:absolute;inset:0;background:radial-gradient(900px 500px at 78% 40%,rgba(91,91,214,.35),transparent 70%),radial-gradient(700px 400px at 10% 100%,rgba(63,182,168,.16),transparent 70%),#0B0D12}}
    .c{{position:absolute;width:560px;height:315px;border-radius:16px;background-size:cover;background-position:center;box-shadow:0 30px 60px rgba(0,0,0,.55),0 0 0 1px rgba(255,255,255,.10)}}
    .t{{position:absolute;left:96px;top:190px;width:640px}}
    .t .row{{display:flex;align-items:center;gap:22px}}
    h1{{margin:0;font-size:84px;font-weight:700;letter-spacing:-2.5px}}
    p{{margin:26px 0 0;font-size:27px;line-height:1.4;color:#B4BCCE;font-weight:400;max-width:560px}}
    </style><div class=bg></div>{fan}
    <div class=t><div class=row>{logo}<h1>PrettyDesk</h1></div><p>Game-aware wallpapers for Windows&nbsp;11. Quiet in the tray, beautiful on the desktop.</p></div>"""
    render(html, "hero", 1600, 640)


# ---- galleries -------------------------------------------------------------------------------------------------------

def gallery(name: str, items: list[tuple[str, str]]) -> None:
    tiles = "".join(
        f'<div class="t" style="background-image:url({art(n, 800)})"><span>{escape(label)}</span></div>' for n, label in items
    )
    html = f"""<!doctype html><meta charset=utf-8><style>{font_css()}
    html,body{{margin:0;width:1600px;height:610px;background:#0B0D12;overflow:hidden}}
    .g{{display:grid;grid-template-columns:repeat(3,1fr);gap:14px;padding:14px}}
    .t{{position:relative;height:282px;border-radius:14px;background-size:cover;background-position:center;box-shadow:inset 0 0 0 1px rgba(255,255,255,.08)}}
    .t span{{position:absolute;left:14px;bottom:14px;padding:6px 12px;border-radius:999px;background:rgba(10,12,18,.62);color:#F1F3F9;font-size:15px;font-weight:600;
      backdrop-filter:blur(8px);letter-spacing:.1px}}
    </style><div class=g>{tiles}</div>"""
    render(html, name, 1600, 610)


# ---- how it works ----------------------------------------------------------------------------------------------------

def how_it_works() -> None:
    steps = [
        ("1", "Detect", "A light check of running program names. Never opens a process beyond query-limited access, never reads memory."),
        ("2", "Match", f"The name is matched against a signed catalog of {len(CATALOG['games'])} games, plus any you add yourself."),
        ("3", "Apply", "A wallpaper rendered for each monitor's resolution replaces the desktop picture, only where it differs."),
        ("4", "Restore", "When the game closes, your default or rotating set returns. Your original wallpaper is always backed up."),
    ]
    cards = "".join(
        f'<div class="s"><div class="n">{n}</div><h3>{t}</h3><p>{escape(d)}</p></div>' + ('<div class="a">→</div>' if n != "4" else "")
        for n, t, d in steps
    )
    html = f"""<!doctype html><meta charset=utf-8><style>{font_css()}
    html,body{{margin:0;width:1600px;height:380px;overflow:hidden;background:linear-gradient(135deg,#14172A,#0E1016 60%,#10201F)}}
    .w{{display:flex;align-items:center;gap:14px;padding:46px 56px}}
    .s{{flex:1;background:rgba(255,255,255,.045);border:1px solid rgba(255,255,255,.10);border-radius:18px;padding:26px 26px 24px;height:236px;box-sizing:border-box}}
    .n{{width:34px;height:34px;border-radius:10px;background:#5B5BD6;color:#fff;font-weight:700;display:flex;align-items:center;justify-content:center;font-size:17px}}
    h3{{margin:18px 0 8px;color:#EEF1F8;font-size:24px;font-weight:650;letter-spacing:-.3px}}
    p{{margin:0;color:#A5AFC3;font-size:16px;line-height:1.5}}
    .a{{color:#6E72D8;font-size:28px;font-weight:600}}
    </style><div class=w>{cards}</div>"""
    render(html, "how-it-works", 1600, 380, "png")


# ---- UI renders ------------------------------------------------------------------------------------------------------

ICONS = {
    "Home": '<path d="M3 11 12 3l9 8M5 10v10h5v-6h4v6h5V10"/>',
    "Library": '<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>',
    "Defaults": '<rect x="3" y="4" width="18" height="14" rx="2.5"/><path d="m3 15 5-5 4 4 3-3 6 5M8 21h8"/>',
    "Settings": '<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9 7 7M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1"/>',
    "About": '<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/>',
}
NAV = [("Home", "Nav_Home"), ("Library", "Nav_Library"), ("Defaults", "Nav_Defaults"), ("Settings", "Nav_Settings"), ("About", "Nav_About")]


def css(theme: str) -> str:
    p = {k: argb(v) for k, v in THEMES[theme].items()}
    return f"""{font_css()}
    :root{{--canvas:{p['DeskCanvasBrush']};--side:{p['DeskSidebarBrush']};--surface:{p['DeskSurfaceBrush']};--soft:{p['DeskSoftBrush']};--veil:{p['DeskVeilBrush']};
      --stroke:{p['DeskStrokeBrush']};--ink:{p['DeskInkBrush']};--muted:{p['DeskMutedBrush']};--accent:{p['DeskAccentBrush']};--accent-text:{p['DeskAccentTextBrush']};
      --accent-soft:{p['DeskAccentSoftBrush']};--on-accent:{p['DeskOnAccentBrush']}}}
    *{{box-sizing:border-box}}
    html,body{{margin:0;width:1600px;height:1000px;overflow:hidden;color:var(--ink);font-size:14px}}
    .desk{{position:absolute;inset:0;background-size:cover;background-position:center}}
    .desk:after{{content:"";position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,.10),rgba(0,0,0,.35))}}
    .win{{position:absolute;left:120px;top:64px;width:1360px;height:872px;border-radius:10px;overflow:hidden;background:var(--canvas);
      box-shadow:0 40px 90px rgba(0,0,0,.50),0 0 0 1px rgba(255,255,255,.12);display:grid;grid-template-rows:36px 1fr;grid-template-columns:220px 1fr}}
    .title{{grid-column:1/3;background:var(--side);display:flex;justify-content:flex-end}}
    .title i{{width:46px;height:36px;display:flex;align-items:center;justify-content:center}}
    .title svg{{width:10px;height:10px;stroke:var(--ink);fill:none;stroke-width:1}}
    .side{{background:var(--side);border-right:1px solid var(--stroke);padding:0 12px;position:relative}}
    .brand{{display:flex;align-items:center;gap:10px;padding:14px 8px 26px;font-size:21px;font-weight:650;letter-spacing:-.4px}}
    .nav{{position:relative;display:flex;align-items:center;gap:12px;height:42px;padding:0 14px;margin:2px 0;border-radius:8px;color:var(--muted);font-weight:500;font-size:14px}}
    .nav svg{{width:18px;height:18px;fill:none;stroke:currentColor;stroke-width:1.6;stroke-linecap:round;stroke-linejoin:round}}
    .nav.on{{background:var(--accent-soft);color:var(--accent-text)}}
    .nav.on:before{{content:"";position:absolute;left:0;top:12px;width:3px;height:18px;border-radius:2px;background:var(--accent-text)}}
    .page{{padding:24px 32px 0;overflow:hidden}}
    h1{{margin:0 0 4px;font-size:26px;font-weight:600;letter-spacing:-.4px}}
    h2{{margin:28px 0 10px;font-size:15px;font-weight:600}}
    .muted{{color:var(--muted)}}
    .card{{background:var(--surface);border:1px solid var(--stroke);border-radius:10px;padding:16px 20px}}
    .btn{{display:inline-flex;align-items:center;height:34px;padding:0 14px;border-radius:6px;border:1px solid var(--stroke);background:var(--soft);font-weight:500;margin-right:8px}}
    .btn.p{{background:var(--accent);border-color:var(--accent);color:var(--on-accent)}}
    .seg{{display:inline-flex;background:var(--soft);border-radius:8px;padding:3px}}
    .seg span{{display:flex;align-items:center;min-height:30px;padding:0 14px;border-radius:6px;color:var(--muted);font-weight:500}}
    .seg .on{{background:var(--accent);color:var(--on-accent)}}
    .search{{display:inline-flex;align-items:center;width:280px;height:34px;padding:0 12px;border-radius:6px;border:1px solid var(--stroke);background:var(--surface);color:var(--muted);margin-right:16px}}
    .chip{{display:inline-block;padding:3px 8px;border-radius:6px;background:var(--soft);font-size:12px}}
    .sw{{width:40px;height:20px;border-radius:10px;position:relative;background:var(--accent);flex:none}}
    .sw:after{{content:"";position:absolute;top:4px;left:23px;width:12px;height:12px;border-radius:6px;background:#fff}}
    .sw.off{{background:transparent;border:1px solid var(--muted)}}
    .sw.off:after{{left:4px;top:3px;background:var(--muted)}}
    .row{{display:flex;align-items:center;justify-content:space-between;padding:12px 0}}
    .row>div:first-child{{padding-right:28px}}
    .row b{{font-weight:600;display:block}}
    .row small{{display:block;color:var(--muted);font-size:13px;margin-top:2px}}
    .div{{height:1px;background:var(--stroke);opacity:.7}}
    .combo{{min-width:160px;height:32px;border:1px solid var(--stroke);border-radius:6px;background:var(--soft);display:flex;align-items:center;justify-content:space-between;padding:0 12px;color:var(--ink)}}
    .grid{{display:flex;flex-wrap:wrap;gap:0 16px}}
    .game{{width:236px;margin-bottom:16px;padding:0;overflow:hidden}}
    .game .img{{height:172px;background-size:cover;background-position:center;display:flex;align-items:center;justify-content:center}}
    .game .nm{{font-weight:600;padding:10px 12px 4px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}}
    .game .ft{{display:flex;align-items:center;justify-content:space-between;padding:0 12px 12px}}
    .ph{{background:linear-gradient(135deg,#2A2F55,#3FB6A8);font-size:44px;font-weight:600;color:rgba(255,255,255,.8)}}
    """


def shell(theme: str, active: str, body: str, wall: str) -> str:
    logo = LOGO.format(w=22, h=19, ink="var(--ink)", accent="var(--accent-text)")
    nav = "".join(
        f'<div class="nav{" on" if k == active else ""}"><svg viewBox="0 0 24 24">{ICONS[k]}</svg>{S(key)}</div>' for k, key in NAV
    )
    controls = (
        '<i><svg viewBox="0 0 10 10"><path d="M0 5h10"/></svg></i><i><svg viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9"/></svg></i>'
        '<i><svg viewBox="0 0 10 10"><path d="M0 0l10 10M10 0 0 10"/></svg></i>'
    )
    return f"""<!doctype html><meta charset=utf-8><style>{css(theme)}</style>
    <div class=desk style="background-image:url({art(wall, 1600)})"></div>
    <div class=win><div class=title>{controls}</div>
      <div class=side><div class=brand>{logo}PrettyDesk</div>{nav}</div>
      <div class=page>{body}</div></div>"""


def toggle(on: bool = True) -> str:
    return f'<div class="sw{"" if on else " off"}"></div>'


def row(title: str, help_: str | None, control: str) -> str:
    h = f"<small>{help_}</small>" if help_ else ""
    return f'<div class="row"><div><b>{title}</b>{h}</div>{control}</div>'


def page_home() -> str:
    mon = "cyberpunk.alt-01_16x9"
    img = art(mon, 960)

    def display(x: int, y: int, w: int, h: int) -> str:
        return (f'<div style="position:absolute;left:{x}px;top:{y}px;width:{w}px;height:{h}px;border:3px solid var(--stroke);border-radius:6px;'
                f'background:url({img}) center/cover"></div>')

    return f"""<h1>{S('Home_Title')}</h1>
    <div class=card style="margin-top:16px;padding:22px 24px"><div style="font-size:20px;font-weight:600">{S('Status_Playing', 'Cyberpunk 2077')}</div>
      <div class=muted style="margin-top:4px">{S('Status_PlayingDetailPosition', 2, 4)}</div>
      <div style="margin-top:20px"><span class="btn p" style="min-width:140px;justify-content:center">{S('Home_NextWallpaper')}</span>
      <span class=btn>{S('Home_PauseOneHour')}</span><span class=btn>{S('Home_PauseUntilResumed')}</span></div></div>
    <div class=card style="margin-top:12px;padding:20px 24px"><div style="font-weight:600;margin-bottom:12px">{S('Home_PreviewLabel')}</div>
      <div style="position:relative;height:276px;width:720px;margin:0 auto">{display(0, 6, 456, 257)}{display(470, 6, 250, 141)}</div>
      <div class=muted style="font-size:13px;margin-top:14px">{S('Home_PrimaryDisplay', 1, 2560, 1440)}<br>{S('Home_DisplayLabel', 2, 1920, 1080)}</div></div>"""


def page_library() -> str:
    games = [
        ("Cyberpunk 2077", "cyberpunk.hero-01", "Ready", True),
        ("Elden Ring", "elden-ring.hero-01", "Ready", True),
        ("Fortnite", "fortnite.hero-01", "Ready", True),
        ("Minecraft", "minecraft.hero-01", "Ready", False),
        ("Hades II", "hades2.hero-01", "Ready", True),
        ("Destiny 2", "destiny2.hero-01", S("Library_ChipDownloading", 42), True),
        ("Diablo IV", "diablo4.hero-01", "Ready", False),
        ("Rocket League", None, S("Library_ChipNotDownloaded"), False),
    ]
    cards = ""
    for name, thumb, chip, on in games:
        if thumb:
            visual = f'<div class=img style="background-image:url({art(thumb + "_thumb", 640)})"></div>'
        else:
            visual = f'<div class="img ph">{name[0]}</div>'
        cards += (f'<div class="card game">{visual}<div class=nm>{name}</div>'
                  f'<div class=ft><span class=chip>{chip}</span>{toggle(on)}</div></div>')
    return f"""<div style="display:flex;justify-content:space-between;align-items:center"><h1>{S('Library_Title')}</h1><span class="btn p" style="margin:0">{S('Library_AddGame')}</span></div>
    <div style="display:flex;align-items:center;margin:16px 0"><span class=search>{S('Library_Search')}</span>
      <div class=seg><span class=on>{S('Library_FilterAll')}</span><span>{S('Library_FilterInstalled')}</span><span>{S('Library_FilterEnabled')}</span></div></div>
    <div class=grid>{cards}</div>"""


def page_settings() -> str:
    combo = '<div class=combo><span>System</span><span class=muted>⌄</span></div>'
    slider = ('<div style="width:220px;position:relative;height:20px"><div style="position:absolute;top:8px;left:0;right:0;height:4px;border-radius:2px;background:var(--soft)"></div>'
              '<div style="position:absolute;top:8px;left:0;width:11%;height:4px;border-radius:2px;background:var(--accent)"></div>'
              '<div style="position:absolute;top:2px;left:calc(11% - 8px);width:16px;height:16px;border-radius:8px;background:var(--accent);box-shadow:0 0 0 3px var(--surface) inset"></div></div>')
    general = "".join([
        row(S("Settings_AppTheme"), S("Settings_AppThemeHelp"), combo), '<div class=div></div>',
        row(S("Settings_StartWithWindows"), S("Settings_StartWithWindowsHelp"), toggle(True)), '<div class=div></div>',
        row(S("Settings_RestoreOnExit"), S("Settings_RestoreOnExitHelp"), toggle(True)), '<div class=div></div>',
        row(S("Settings_DesktopClock"), S("Settings_DesktopClockHelp"), toggle(False)),
    ])
    detection = "".join([
        row(S("Settings_DetectGames"), S("Settings_DetectGamesHelp"), toggle(True)), '<div class=div></div>',
        f'<div style="padding:12px 0"><b style="font-weight:600">{S("Settings_PollSeconds")}</b> <b style="font-weight:600;color:var(--accent-text)">{S("Settings_PollSecondsValue", 2)}</b>'
        f'<div style="margin-top:8px">{slider}</div></div>', '<div class=div></div>',
        row(S("Settings_PrefetchGames"), S("Settings_PrefetchGamesHelp"), toggle(True)),
    ])
    return f"""<div style="max-width:760px"><h1>{S('Settings_Title')}</h1>
    <h2>{S('Settings_General')}</h2><div class=card style="padding:4px 20px">{general}</div>
    <h2>{S('Settings_Detection')}</h2><div class=card style="padding:4px 20px">{detection}</div></div>"""


def ui_renders() -> None:
    walls = {"home": "cyberpunk.alt-01_16x9", "library": "ios.dusk_16x9", "settings": "ds-01_16x9"}
    pages = {"home": ("Home", page_home), "library": ("Library", page_library), "settings": ("Settings", page_settings)}
    for theme in ("light", "dark"):
        for key, (active, build) in pages.items():
            render(shell(theme, active, build(), walls[key]), f"ui-{key}-{theme}", 1600, 1000)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    hero()
    gallery("gallery-games", [
        ("cyberpunk.hero-01_16x9", "Cyberpunk 2077"), ("fortnite.hero-01_16x9", "Fortnite"), ("minecraft.hero-01_16x9", "Minecraft"),
        ("rocket-league.octane-aerial_16x9", "Rocket League"), ("r6siege.mood-01_16x9", "Rainbow Six Siege X"), ("cod.hero-01_16x9", "Call of Duty"),
    ])
    gallery("gallery-defaults", [
        ("mb-01_16x9", "Matte Black"), ("pa-01_16x9", "Painted Landscapes"), ("ios.tide_16x9", "Liquid Glass"),
        ("sb-01_16x9", "Sage & Botanical"), ("ds-01_16x9", "Deep Space"), ("wm-01_16x9", "Warm Minimal"),
    ])
    how_it_works()
    ui_renders()
    shutil.rmtree(WORK, ignore_errors=True)
