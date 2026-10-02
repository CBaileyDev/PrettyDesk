"""Check local file links in authored repository Markdown; no network or anchor checks.

Run from any directory: python tools/check_docs.py
Generated prompt sheets, evidence, images and build outputs are intentionally excluded.
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path
from urllib.parse import unquote, urlsplit

FENCE = re.compile(r"^\s*(`{3,}|~{3,})")
INLINE_CODE = re.compile(r"(`+).*?\1")
LINK = re.compile(r"!?\[[^\]\n]*\]\(\s*(?:<([^>\n]+)>|([^\s)]+))(?:\s+[^)]*)?\)")
DEFINITION = re.compile(r"^\s*\[[^\]\n]+\]:\s*(?:<([^>\n]+)>|([^\s]+))")


def documents(root: Path) -> list[Path]:
    paths = [root / name for name in ("README.md", "AGENTS.md", "CLAUDE.md")]
    paths.extend(root / folder / "README.md" for folder in ("src", "tests", "tools", "art", "content"))
    for folder in ("docs", "docs/adr", "docs/updates"):
        paths.extend(sorted((root / folder).glob("*.md")))
    return paths


def local_links(path: Path):
    fence = None
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        marker = FENCE.match(line)
        if marker:
            token = marker.group(1)
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None
            continue
        if fence is not None:
            continue
        line = INLINE_CODE.sub("", line)
        matches = list(LINK.finditer(line))
        definition = DEFINITION.match(line)
        if definition:
            matches.append(definition)
        for match in matches:
            target = match.group(1) or match.group(2)
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            yield line_number, target, unquote(parsed.path)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    failures = []
    checked = 0
    paths = documents(root)
    for path in paths:
        label = path.relative_to(root)
        if not path.is_file():
            failures.append(f"{label}: required navigation document is missing")
            continue
        for line_number, target, relative_path in local_links(path):
            checked += 1
            destination = (root / relative_path.lstrip("/") if relative_path.startswith("/")
                           else path.parent / relative_path)
            if not destination.exists():
                failures.append(f"{label}:{line_number}: missing local target {target}")
    for failure in failures:
        print(failure)
    print(f"Checked {len(paths)} authored documents and {checked} local file links; {len(failures)} error(s).")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
