"""Repository layout. Everything resolves from the repo root, found by walking up to ``PrettyDesk.sln``."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


def find_repo_root(start: Path | None = None) -> Path:
    here = (start or Path.cwd()).resolve()
    for candidate in (here, *here.parents):
        if (candidate / "PrettyDesk.sln").exists():
            return candidate
    # Fall back to the location of this file: tools/assetpipe/assetpipe/paths.py
    return Path(__file__).resolve().parents[3]


@dataclass(frozen=True)
class Layout:
    root: Path

    @property
    def prompts(self) -> Path:
        return self.root / "art" / "prompts"

    @property
    def prompts_md(self) -> Path:
        return self.root / "art" / "PROMPTS.md"

    @property
    def raw(self) -> Path:
        return self.root / "art" / "raw"

    @property
    def out(self) -> Path:
        return self.root / "art" / "out"

    @property
    def review(self) -> Path:
        return self.root / "art" / "review"

    @property
    def catalog_src(self) -> Path:
        return self.root / "content" / "catalog.src.json"

    @property
    def publish_dir(self) -> Path:
        return self.out / "publish"

    def raw_file(self, pack_id: str, wallpaper_id: str, suffix: str) -> Path:
        return self.raw / pack_id / f"{wallpaper_id}_{suffix}.png"

    def out_dir(self, pack_id: str) -> Path:
        return self.out / pack_id

    @staticmethod
    def default() -> "Layout":
        return Layout(find_repo_root())
