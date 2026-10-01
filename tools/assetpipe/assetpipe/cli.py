"""``assetpipe`` command line (SPEC section 8)."""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

from . import commands, lint, publish, render_prompts, review, starter
from .packs import PackError, find_pack, load_all
from .paths import Layout


def _load(layout: Layout):
    if not layout.prompts.exists():
        raise PackError(f"{layout.prompts} does not exist")
    return load_all(layout.prompts)


def cmd_prompts(layout: Layout, args: argparse.Namespace) -> int:
    packs = _load(layout)
    findings = lint.lint_all(packs)
    errors = [f for f in findings if f.severity == "error"]
    for f in findings:
        print(f, file=sys.stderr)
    if errors:
        print(f"\n{len(errors)} prompt error(s); art/PROMPTS.md was not written.", file=sys.stderr)
        return 1
    text = render_prompts.render(packs)
    if args.check:
        current = layout.prompts_md.read_text(encoding="utf-8") if layout.prompts_md.exists() else ""
        if current != text:
            print("art/PROMPTS.md is out of date; run 'assetpipe prompts'.", file=sys.stderr)
            return 1
        print("art/PROMPTS.md is up to date.")
        return 0
    layout.prompts_md.write_text(text, encoding="utf-8", newline="\n")
    total = sum(len(p.wallpapers) for p in packs)
    print(f"Wrote {layout.prompts_md.relative_to(layout.root)}: {len(packs)} packs, {total} wallpapers, {len(findings)} warning(s).")
    return 0


def cmd_lint(layout: Layout, args: argparse.Namespace) -> int:
    packs = _load(layout)
    if args.pack:
        packs = [find_pack(packs, args.pack)]
    findings = lint.lint_all(packs) if not args.pack else lint.lint_pack(packs[0])
    for f in findings:
        print(f)
    errors = sum(1 for f in findings if f.severity == "error")
    print(f"{len(packs)} pack(s): {errors} error(s), {len(findings) - errors} warning(s).")
    return 1 if errors else 0


def cmd_status(layout: Layout, args: argparse.Namespace) -> int:
    print(commands.render_status(commands.collect_status(layout, _load(layout))))
    return 0


def cmd_validate(layout: Layout, args: argparse.Namespace) -> int:
    packs = _load(layout)
    selected = [find_pack(packs, args.pack)] if args.pack else packs
    problems = [p for pack in selected for p in commands.validate_pack(layout, pack)]
    for p in problems:
        print(p)
    print(f"{len(problems)} problem(s).")
    return 1 if problems else 0


def cmd_build(layout: Layout, args: argparse.Namespace) -> int:
    pack = find_pack(_load(layout), args.pack)
    only = set(args.variant) if args.variant else None
    built = []
    failed = False
    for wp in pack.wallpapers:
        if args.wallpaper and wp.id not in args.wallpaper:
            continue
        if not layout.raw_file(pack.id, wp.id, "L").exists():
            print(f"skip   {wp.id}: no L master")
            continue
        result = commands.build_wallpaper(layout, pack, wp, only)
        built.append(result)
        status = "FAILED" if result.errors else "ok"
        print(f"{status:6} {wp.id}: {', '.join(result.variants) or 'nothing built'}")
        for message in result.errors:
            print(f"         error: {message}")
            failed = True
        for message in result.warnings:
            print(f"         warn:  {message}")
        if result.raw_sha256 and not result.errors:
            text = pack.path.read_text(encoding="utf-8")
            updated = commands.update_raw_hashes(text, wp.id, result.raw_sha256)
            if updated != text:
                pack.path.write_text(updated, encoding="utf-8", newline="\n")
    existing = commands.load_manifest(layout, pack.id)
    manifest_path = commands.write_manifest(layout, pack, built)
    merged = json.loads(manifest_path.read_text(encoding="utf-8"))
    for key, value in existing.get("wallpapers", {}).items():
        merged["wallpapers"].setdefault(key, value)
    manifest_path.write_text(json.dumps(merged, indent=2) + "\n", encoding="utf-8")
    print(f"\nNext: assetpipe review --pack {pack.id}")
    return 1 if failed else 0


def cmd_review(layout: Layout, args: argparse.Namespace) -> int:
    pack = find_pack(_load(layout), args.pack)
    path = review.write_review(layout, pack, commands.load_manifest(layout, pack.id))
    print(f"Wrote {path}. Open it in a browser.")
    return 0


def cmd_publish(layout: Layout, args: argparse.Namespace) -> int:
    pack = find_pack(_load(layout), args.pack)
    try:
        manifest, problems = publish.check_publishable(layout, pack)
        if problems:
            for p in problems:
                print(p, file=sys.stderr)
            raise publish.PublishError("refusing to publish: fix the items above")
        src = publish.update_catalog_source(layout, pack, manifest)
        if not str(src.get("contentBaseUrl", "")).startswith("https://"):
            raise publish.PublishError("catalog.src.json needs an https contentBaseUrl before publishing")
        entry = next(p for p in src["packs"] if p["id"] == pack.id)
        publish.stage_upload(layout, pack, entry, entry["version"])
        catalog_json = layout.publish_dir / "catalog.json"
        catalog_json.write_text(json.dumps(src, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
        print(f"Staged pack {pack.id} v{entry['version']} and catalog {src['catalogVersion']} in {layout.publish_dir}")
        if args.dry_run:
            print("Dry run: catalog.src.json not changed, nothing signed or uploaded.")
            return 0
        publish.sign_catalog(layout, catalog_json)
        publish.upload(layout, os.environ.get("PRETTYDESK_UPLOAD_CMD"))
        layout.catalog_src.write_text(json.dumps(src, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
        print("Published. Commit content/catalog.src.json.")
        return 0
    except publish.PublishError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 1


def cmd_starter(layout: Layout, args: argparse.Namespace) -> int:
    packs = _load(layout)
    try:
        report = starter.build_starter(layout, packs)
    except starter.StarterError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 1
    print(f"Starter set: {len(report.variants)} wallpapers + {len(report.thumbs)} thumbnails, {report.total_bytes / 1_048_576:.1f} MB (budget 60 MB) in content/starter/")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="assetpipe", description="PrettyDesk asset pipeline")
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("prompts", help="lint art/prompts/*.yaml and regenerate art/PROMPTS.md")
    p.add_argument("--check", action="store_true", help="fail when art/PROMPTS.md is stale instead of writing it")
    p.set_defaults(fn=cmd_prompts)

    p = sub.add_parser("lint", help="lint prompts only")
    p.add_argument("--pack")
    p.set_defaults(fn=cmd_lint)

    sub.add_parser("status", help="which raw masters exist for every wallpaper").set_defaults(fn=cmd_status)

    p = sub.add_parser("validate", help="raw files decodable, large enough, sane aspect")
    p.add_argument("--pack")
    p.set_defaults(fn=cmd_validate)

    p = sub.add_parser("build", help="upscale, crop, dither, encode into art/out/")
    p.add_argument("--pack", required=True)
    p.add_argument("--wallpaper", action="append", help="limit to a wallpaper id (repeatable)")
    p.add_argument("--variant", action="append", help="limit to a variant key (repeatable)")
    p.set_defaults(fn=cmd_build)

    p = sub.add_parser("review", help="HTML contact sheet with safe-zone overlays")
    p.add_argument("--pack", required=True)
    p.set_defaults(fn=cmd_review)

    sub.add_parser("starter", help="assemble content/starter (installer's offline wallpapers) from approved builds").set_defaults(fn=cmd_starter)

    p = sub.add_parser("publish", help="update catalog, sign, upload")
    p.add_argument("--pack", required=True)
    p.add_argument("--dry-run", action="store_true")
    p.set_defaults(fn=cmd_publish)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return args.fn(Layout.default(), args)
    except PackError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 1
