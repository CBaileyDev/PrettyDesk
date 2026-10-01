#!/usr/bin/env python3
"""Bulk-add strings from a file of `KEY|text` lines (blank lines and # comments ignored), then regenerate the designer."""
import sys
sys.path.insert(0, __file__.rsplit("/", 1)[0])
import strings  # noqa: E402

items = strings.read()
for line in open(sys.argv[1], encoding="utf-8"):
    line = line.rstrip("\n")
    if not line.strip() or line.startswith("#"):
        continue
    key, text = line.split("|", 1)
    items[key] = (text.replace("\\n", "\n"), items.get(key, ("", ""))[1])
strings.write(items)
strings.DESIGNER.write_text(strings.designer_text(items), encoding="utf-8")
print(f"{len(items)} strings")
