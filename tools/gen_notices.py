#!/usr/bin/env python3
"""Generate src/PrettyDesk.App/THIRD-PARTY-NOTICES.txt from the restored NuGet graph of the App project.

  gen_notices.py          write the file (run `dotnet restore src/PrettyDesk.App` first)
  gen_notices.py --check  exit 1 when the committed file is out of date

Only packages that ship in the app (they have runtime or compile assets) are listed; build-time analyzers and source
generators (CsWin32, MinVer, ...) are not redistributed. Each entry names the package, version, license and copyright from its
.nuspec; the common license texts are printed once at the end. The About page shows this file (SPEC §7.5).
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "PrettyDesk.App" / "obj" / "project.assets.json"
OUT = ROOT / "src" / "PrettyDesk.App" / "THIRD-PARTY-NOTICES.txt"

EXTRAS = [
    (".NET runtime, base class libraries and Windows Presentation Foundation (bundled: the app is self-contained)",
     "MIT", "Copyright (c) .NET Foundation and Contributors", "https://github.com/dotnet/runtime, https://github.com/dotnet/wpf"),
    ("Skia graphics library (compiled into SkiaSharp's native assets)",
     "BSD-3-Clause", "Copyright (c) 2011 Google Inc. All rights reserved.", "https://skia.org"),
]

MIT = """MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files
(the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE."""

BSD3 = """BSD 3-Clause License

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following
conditions are met: (1) Redistributions of source code must retain the above copyright notice, this list of conditions and the
following disclaimer. (2) Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the
following disclaimer in the documentation and/or other materials provided with the distribution. (3) Neither the name of the
copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE."""

APACHE = ("Apache License, Version 2.0: licensed under the Apache License, Version 2.0 (the \"License\"); you may not use these files "
          "except in compliance with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0. "
          "Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "
          "\"AS IS\" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.")

TEXTS = {"MIT": MIT, "BSD-3-Clause": BSD3, "Apache-2.0": APACHE}


def nuspec(folder: Path) -> ET.Element | None:
    for path in folder.glob("*.nuspec"):
        text = re.sub(r' xmlns="[^"]+"', "", path.read_text(encoding="utf-8-sig"), count=1)
        return ET.fromstring(text).find("metadata")
    return None


def collect() -> list[dict]:
    if not ASSETS.exists():
        sys.exit(f"{ASSETS} not found; run `dotnet restore src/PrettyDesk.App` first.")
    assets = json.loads(ASSETS.read_text(encoding="utf-8"))
    package_root = Path(next(iter(assets["packageFolders"])))
    shipped: set[str] = set()
    for target in assets["targets"].values():
        for name, entry in target.items():
            if entry.get("type") == "package" and any(k in entry for k in ("runtime", "compile", "runtimeTargets")):
                shipped.add(name)
    items = []
    for name in sorted(shipped, key=str.lower):
        library = assets["libraries"][name]
        meta = nuspec(package_root / library["path"])
        if meta is None:
            continue
        def text(tag):
            node = meta.find(tag)
            return (node.text or "").strip() if node is not None and node.text else ""
        license_node = meta.find("license")
        license_id = (license_node.text or "").strip() if license_node is not None and license_node.get("type") == "expression" else ""
        items.append({
            "id": name.split("/")[0], "version": name.split("/")[1], "license": license_id or text("licenseUrl") or "see package",
            "copyright": text("copyright"), "authors": text("authors"), "url": text("projectUrl") or text("repository"),
        })
    return items


def render(items: list[dict]) -> str:
    lines = ["PrettyDesk third-party notices", "(generated by tools/gen_notices.py; do not edit)", "",
             "PrettyDesk is free software built on the components below. Each is used under its own license.", "", "=" * 78, ""]
    used = set()
    for item in items:
        lines.append(f"{item['id']} {item['version']}")
        if item["authors"]:
            lines.append(f"  Authors:   {item['authors']}")
        if item["copyright"]:
            lines.append(f"  Copyright: {item['copyright']}")
        lines.append(f"  License:   {item['license']}")
        if item["url"]:
            lines.append(f"  Project:   {item['url']}")
        lines.append("")
        used.add(item["license"])
    for name, license_id, copyright_, url in EXTRAS:
        lines += [name, f"  Copyright: {copyright_}", f"  License:   {license_id}", f"  Project:   {url}", ""]
        used.add(license_id)
    lines += ["=" * 78, "", "License texts", ""]
    for license_id in sorted(used):
        text = TEXTS.get(license_id)
        if text:
            lines += [f"--- {license_id} ---", text, ""]
        elif license_id and not license_id.startswith("http"):
            lines += [f"--- {license_id} ---", f"See https://licenses.nuget.org/{license_id}", ""]
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    text = render(collect())
    if "--check" in sys.argv:
        if not OUT.exists() or OUT.read_text(encoding="utf-8") != text:
            print("THIRD-PARTY-NOTICES.txt is out of date; run tools/gen_notices.py", file=sys.stderr)
            return 1
        print("THIRD-PARTY-NOTICES.txt is up to date.")
        return 0
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"Wrote {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
