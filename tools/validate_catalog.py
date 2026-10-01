#!/usr/bin/env python3
"""Validate content/catalog.src.json (or another catalog) against content/catalog.schema.json (SPEC 6.2, content.yml)."""
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "content" / "catalog.src.json"
    schema = json.loads((ROOT / "content" / "catalog.schema.json").read_text(encoding="utf-8"))
    document = json.loads(target.read_text(encoding="utf-8"))
    errors = sorted(Draft202012Validator(schema).iter_errors(document), key=lambda e: list(e.absolute_path))
    for error in errors[:30]:
        where = "/".join(str(p) for p in error.absolute_path) or "(root)"
        print(f"{where}: {error.message}", file=sys.stderr)
    if errors:
        print(f"{len(errors)} schema error(s) in {target.name}", file=sys.stderr)
        return 1
    print(f"{target.name} matches catalog.schema.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
