import json

import numpy as np
import pytest
from PIL import Image

from assetpipe import commands, imaging, publish, render_prompts, review
from assetpipe.packs import load_pack

from .conftest import pack_doc, wallpaper, write_pack


def make_masters(layout, pack_id, wid, with_u=True, with_p=False):
    folder = layout.raw / pack_id
    folder.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(5)
    def img(w, h):
        base = np.tile(np.linspace(20, 80, w, dtype=np.float32), (h, 1))
        arr = np.stack([base, base * 0.8, base * 1.2], axis=2) + rng.normal(0, 1.5, (h, w, 3))
        return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB")
    img(1920, 1080).save(folder / f"{wid}_L.png")
    if with_u:
        img(2880, 960).save(folder / f"{wid}_U.png")
    if with_p:
        img(1152, 2048).save(folder / f"{wid}_P.png")


def setup_pack(layout, **wp_overrides):
    doc = pack_doc(wallpapers=[wallpaper("mb-01", **wp_overrides)])
    return load_pack(write_pack(layout.prompts, doc))


def test_status_reports_missing_and_present_masters(layout):
    pack = setup_pack(layout)
    make_masters(layout, pack.id, "mb-01", with_u=False)
    [row] = commands.collect_status(layout, [pack])
    assert row.have == {"L": True, "U": False, "P": False}
    assert not row.complete and not row.built
    text = commands.render_status([row])
    assert "mb-01" in text and "1 wallpapers" in text


def test_validate_flags_orphans_and_bad_masters(layout):
    pack = setup_pack(layout, portrait=False)
    folder = layout.raw / pack.id
    folder.mkdir(parents=True)
    Image.new("RGB", (1000, 560)).save(folder / "mb-01_U.png")
    problems = commands.validate_pack(layout, pack)
    assert any("no L master" in p for p in problems)
    assert any("below" in p for p in problems)


def test_invalid_source_is_rejected_before_ai_upscale(layout, monkeypatch):
    pack = setup_pack(layout)
    folder = layout.raw / pack.id
    folder.mkdir(parents=True)
    Image.new("RGB", (640, 360)).save(folder / "mb-01_L.png")
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: "/fake/upscaler")
    monkeypatch.setattr(imaging, "upscale_4x", lambda *_: pytest.fail("Invalid masters must not enter the GPU model"))
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0])
    assert any("below" in problem for problem in built.errors)
    assert not built.variants


def test_build_writes_every_variant_at_canonical_size(layout, monkeypatch):
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: None)
    pack = setup_pack(layout)
    make_masters(layout, pack.id, "mb-01", with_u=True, with_p=True)
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0])
    assert built.errors == []
    assert set(built.variants) == {"16x9", "16x10", "3x2", "21x9", "32x9", "9x16"}
    for key, ref in built.variants.items():
        assert imaging.sniff_dimensions((layout.out_dir(pack.id) / ref.path).read_bytes()) == (ref.width, ref.height)
        assert ref.bytes < 8 * 1024 * 1024
    assert built.thumb and (built.thumb.width, built.thumb.height) == (640, 360)
    assert set(built.raw_sha256) == {"L", "U", "P"}


def test_build_without_portrait_master_warns_and_skips_9x16(layout, monkeypatch):
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: None)
    pack = setup_pack(layout)
    make_masters(layout, pack.id, "mb-01", with_u=True, with_p=False)
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0], only={"16x9", "9x16"})
    assert "9x16" not in built.variants and any("portrait" in w for w in built.warnings)


def test_build_requires_an_l_master(layout):
    pack = setup_pack(layout)
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0])
    assert built.errors and not built.variants


def test_update_raw_hashes_replaces_block_map_without_corrupting_yaml():
    import yaml
    text = 'wallpapers:\n- id: a\n  rawSha256:\n    L: old\n    U: old-wide\n  approved: false\n- id: b\n  rawSha256: {L: keep}\n'
    result = yaml.safe_load(commands.update_raw_hashes(text, 'a', {'L': 'new'}))
    assert result['wallpapers'][0] == {'id': 'a', 'rawSha256': {'L': 'new'}, 'approved': False}
    assert result['wallpapers'][1]['rawSha256'] == {'L': 'keep'}


def test_update_raw_hashes_rewrites_only_the_target_wallpaper():
    text = "wallpapers:\n  - id: a-01\n    rawSha256: {}\n    approved: false\n  - id: a-02\n    rawSha256: {}\n"
    out = commands.update_raw_hashes(text, "a-02", {"L": "ab", "U": "cd"})
    assert 'rawSha256: {L: "ab", U: "cd"}' in out
    assert out.split("\n")[2] == "    rawSha256: {}"
    assert commands.update_raw_hashes(text, "missing", {"L": "x"}) == text


def test_review_sheet_has_overlays_and_flags_unapproved(layout, monkeypatch):
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: None)
    pack = setup_pack(layout)
    make_masters(layout, pack.id, "mb-01")
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0])
    commands.write_manifest(layout, pack, [built])
    path = review.write_review(layout, pack, commands.load_manifest(layout, pack.id))
    html = path.read_text(encoding="utf-8")
    assert 'class="icons"' in html and 'class="task"' in html and 'class="dot"' in html
    assert "awaiting approval" in html and "mb-01_16x9.jpg" in html


def test_publish_refuses_unapproved_and_stale_builds(layout, monkeypatch):
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: None)
    pack = setup_pack(layout)
    make_masters(layout, pack.id, "mb-01")
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0])
    commands.write_manifest(layout, pack, [built])
    _, problems = publish.check_publishable(layout, pack)
    assert any("not approved" in p for p in problems)

    pack.wallpapers[0].approved = True
    _, problems = publish.check_publishable(layout, pack)
    assert problems == []
    (layout.out_dir(pack.id) / "mb-01_16x9.jpg").write_bytes(b"tampered")
    _, problems = publish.check_publishable(layout, pack)
    assert any("changed since the build" in p for p in problems)


def test_publish_updates_catalog_source_with_hashes_and_bumps_versions(layout, monkeypatch):
    from datetime import datetime, timezone

    monkeypatch.setattr(imaging, "realesrgan_path", lambda: None)
    pack = setup_pack(layout)
    pack.wallpapers[0].approved = True
    make_masters(layout, pack.id, "mb-01")
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0])
    commands.write_manifest(layout, pack, [built])
    layout.catalog_src.parent.mkdir(parents=True, exist_ok=True)
    layout.catalog_src.write_text(json.dumps({
        "schemaVersion": 1, "catalogVersion": "2026.10.01.1", "minAppVersion": "1.0.0", "contentBaseUrl": "https://c.example/v1/",
        "games": [], "collections": [], "packs": [{"id": pack.id, "kind": "default", "version": 0, "title": "Matte Black", "wallpapers": []}],
    }))
    manifest, problems = publish.check_publishable(layout, pack)
    assert problems == []
    day = datetime(2026, 10, 1, tzinfo=timezone.utc)
    src = publish.update_catalog_source(layout, pack, manifest, today=day)
    assert src["catalogVersion"] == "2026.10.01.2"
    entry = src["packs"][0]
    assert entry["version"] == 1
    wp = entry["wallpapers"][0]
    assert wp["variants"]["16x9"]["path"] == "packs/default.matte-black/v1/mb-01_16x9.jpg"
    assert len(wp["variants"]["16x9"]["sha256"]) == 64 and wp["thumb"]["w"] == 640
    assert wp["focal"] == {"x": 0.66, "y": 0.5}

    publish.stage_upload(layout, pack, entry, entry["version"])
    assert (layout.publish_dir / "packs" / pack.id / "v1" / "mb-01_16x9.jpg").exists()


def test_publish_without_catalog_pack_entry_is_a_clear_error(layout, monkeypatch):
    pack = setup_pack(layout)
    layout.catalog_src.parent.mkdir(parents=True, exist_ok=True)
    layout.catalog_src.write_text(json.dumps({"packs": []}))
    try:
        publish.update_catalog_source(layout, pack, {"wallpapers": {}})
    except publish.PublishError as ex:
        assert "has no pack" in str(ex)
    else:
        raise AssertionError


def test_upload_requires_a_configured_command(layout):
    try:
        publish.upload(layout, None)
    except publish.PublishError as ex:
        assert "PRETTYDESK_UPLOAD_CMD" in str(ex)
    else:
        raise AssertionError


def test_prompts_markdown_contains_copy_blocks_and_file_names(layout):
    pack = setup_pack(layout)
    md = render_prompts.render([pack])
    assert "art/raw/default.matte-black/mb-01_L.png" in md and "_U.png" in md and "_P.png" in md
    assert md.count("```text") == 1 + 3  # style bible + three prompts
    assert "1 packs · 1 wallpapers" in md


def test_starter_set_copies_the_starter_variant_and_every_thumbnail_and_refuses_unapproved(layout, monkeypatch):
    from assetpipe import starter

    monkeypatch.setattr(imaging, "realesrgan_path", lambda: None)
    pack = setup_pack(layout, starter=True)
    make_masters(layout, pack.id, "mb-01")
    built = commands.build_wallpaper(layout, pack, pack.wallpapers[0], only={"16x9"})
    commands.write_manifest(layout, pack, [built])

    with pytest.raises(starter.StarterError, match="not approved"):
        starter.build_starter(layout, [pack])

    pack.wallpapers[0].approved = True
    report = starter.build_starter(layout, [pack])
    assert report.variants == ["mb-01_16x9.jpg"] and report.thumbs == ["mb-01_thumb.jpg"]
    assert (layout.root / "content" / "starter" / "mb-01_16x9.jpg").exists()
    assert report.total_bytes > 0
