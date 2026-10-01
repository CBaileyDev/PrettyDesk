from __future__ import annotations

import textwrap
from pathlib import Path

import pytest
import yaml

from assetpipe.paths import Layout

LANDSCAPE = textwrap.dedent(
    """\
    A 16:9 landscape desktop wallpaper. An ultra-minimal scene of a single smooth sand dune made of fine
    matte-black sand. Its sharp, curved crest sweeps from the lower right toward the center of the frame.
    A faint cool silver rim light grazes the crest from the upper right, revealing delicate wind ripples on
    the slope; everything else falls away into deep charcoal and true black. The sky is a seamless
    near-black gradient with no stars.
    Style: high-end fine-art photography with a medium-format look; sculptural, quiet and luxurious.
    Palette: #050506, #0E0F11, #1A1B1E with a subtle silver highlight #AEB4BC. Mood: calm, focused, premium.
    Composition: the dune crest occupies the right half, about 55–75% across and 40–60% down. The left 15%
    and the bottom 8% stay calm and low-detail (desktop icons and taskbar go there). Keep every important
    element inside the central 80% of the width and the middle 70% of the height so the image can be
    cropped to other screen shapes.
    Technical: highest available resolution; crisp detail on the ridge; perfectly smooth, banding-free dark
    gradients; no noise, no vignette, no border or frame.
    Strictly no text, letters, numbers, logos, symbols, watermarks, signatures, people, animals or UI elements.
    """
)
ULTRAWIDE = textwrap.dedent(
    """\
    Extend this exact image into an ultra-wide 3:1 panorama by continuing the scene naturally to the left
    and right. Do not change, move, re-light or re-style anything that already exists. New area on the left:
    calm, near-black empty space with only the faintest suggestion of a low dune. New area on the right: a
    second, smaller dune ridge fading into darkness, catching a whisper of the same silver rim light. Same
    lighting direction, palette, sand texture and level of detail. No seams, no repeated or mirrored shapes,
    no text or logos.
    """
)
PORTRAIT = textwrap.dedent(
    """\
    Using the attached image as the style and content reference, recompose the same scene as a 9:16
    portrait wallpaper. The dune crest rises diagonally from the lower right toward the upper center, with
    its brightest rim light at about 40% of the height. Same silver rim lighting, matte-black sand, palette
    and photographic style. Keep the bottom 10% calm and dark. Strictly no text, letters, logos, watermarks
    or UI.
    """
)


def wallpaper(wid: str = "mb-01", **overrides) -> dict:
    base = {
        "id": wid, "title": "Obsidian Dune", "role": "default", "tone": "dark", "setupMatch": ["black", "rgb"],
        "tags": ["minimal"], "starter": False, "accent": "#AEB4BC", "focal": {"x": 0.66, "y": 0.5},
        "upscaler": "x4plus", "grain": 0.25, "portrait": True,
        "prompts": {"landscape": LANDSCAPE, "ultrawide": ULTRAWIDE, "portrait": PORTRAIT},
        "reviewNotes": "", "approved": False, "rawSha256": {},
    }
    base.update(overrides)
    return base


def pack_doc(pack_id: str = "default.matte-black", kind: str = "default", wallpapers: list[dict] | None = None, **extra) -> dict:
    doc = {
        "pack": pack_id, "kind": kind, "title": "Matte Black",
        "styleBible": "We are creating a cohesive series of premium desktop wallpapers in near-black tones with one cool rim light. "
                      "Keep every image calm, sculptural and quiet, with generous negative space and smooth gradients throughout. "
                      "Each image is fine-art photography with a medium-format look, rich but never crushed darks, and no text of any kind. "
                      "Compositions keep the left edge and the bottom edge calm so desktop icons and the taskbar stay legible.",
        "wallpapers": wallpapers if wallpapers is not None else [wallpaper()],
    }
    doc.update(extra)
    return doc


def write_pack(directory: Path, doc: dict) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / f"{doc['pack']}.yaml"
    path.write_text(yaml.safe_dump(doc, sort_keys=False, allow_unicode=True, width=10_000), encoding="utf-8")
    return path


@pytest.fixture
def layout(tmp_path: Path) -> Layout:
    (tmp_path / "PrettyDesk.sln").write_text("")
    return Layout(tmp_path)
