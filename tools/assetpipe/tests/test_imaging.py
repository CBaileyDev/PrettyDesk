import io

import numpy as np
from PIL import Image

from assetpipe import imaging
from assetpipe.variants import variant


def gradient_master(w=2000, h=1125, lo=20, hi=40):
    ramp = np.linspace(lo, hi, w, dtype=np.float32)
    arr = np.tile(ramp, (h, 1))
    return Image.fromarray(np.stack([arr, arr, arr], axis=2).astype(np.uint8), "RGB")


def test_load_master_converts_to_rgb_and_rejects_garbage(tmp_path):
    Image.new("RGBA", (1600, 900), (10, 20, 30, 255)).save(tmp_path / "a.png")
    assert imaging.load_master(tmp_path / "a.png").mode == "RGB"
    (tmp_path / "b.png").write_bytes(b"not an image")
    try:
        imaging.load_master(tmp_path / "b.png")
    except imaging.ImageProblem as ex:
        assert "cannot decode" in str(ex)
    else:
        raise AssertionError("expected ImageProblem")


def test_check_master_minimum_size_and_ratio():
    assert imaging.check_master(Image.new("RGB", (1920, 1080)), "L") == []
    assert any("below" in p for p in imaging.check_master(Image.new("RGB", (1200, 675)), "L"))
    assert any("not close to 16:9" in p for p in imaging.check_master(Image.new("RGB", (1600, 1600)), "L"))
    assert imaging.check_master(Image.new("RGB", (3072, 1024)), "U") == []
    assert any("narrower than 21:9" in p for p in imaging.check_master(Image.new("RGB", (1920, 1080)), "U"))
    assert imaging.check_master(Image.new("RGB", (1152, 2048)), "P") == []


def test_small_deficits_use_the_plain_resize_and_hit_exact_pixels(monkeypatch):
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: "/usr/bin/fake")  # would be called if the policy asked for it
    monkeypatch.setattr(imaging, "upscale_4x", lambda *_: (_ for _ in ()).throw(AssertionError("AI upscale must not run")))
    master = Image.new("RGB", (3840, 2160), (40, 60, 90))
    result = imaging.render_variant(master, variant("16x10"), 0.6, 0.5, "x4plus", 0.0)
    assert result.image.size == (3840, 2400)
    assert not result.upscaled and not result.soft
    assert 1.0 < result.enlarge <= 1.25


def test_big_deficit_without_the_model_is_flagged_soft_but_still_exact(monkeypatch):
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: None)
    master = Image.new("RGB", (1600, 900), (40, 60, 90))
    result = imaging.render_variant(master, variant("16x9"), 0.5, 0.5, "x4plus", 0.0)
    assert result.soft and not result.upscaled and result.image.size == (3840, 2160)


def test_big_deficit_with_the_model_runs_it_once(monkeypatch):
    calls = []
    monkeypatch.setattr(imaging, "realesrgan_path", lambda: "/usr/bin/fake")
    monkeypatch.setattr(imaging, "upscale_4x", lambda img, model: calls.append(model) or img.resize((img.width * 4, img.height * 4)))
    master = Image.new("RGB", (1600, 900), (40, 60, 90))
    result = imaging.render_variant(master, variant("16x9"), 0.5, 0.5, "x4plus-anime", 0.0)
    assert calls == ["x4plus-anime"] and result.upscaled and not result.soft and result.image.size == (3840, 2160)


def test_crop_follows_the_focal_point():
    master = Image.new("RGB", (4000, 2250), (0, 0, 0))
    master.paste((255, 255, 255), (3600, 0, 4000, 2250))  # a white strip on the far right
    left = imaging.render_variant(master, variant("3x2"), 0.0, 0.5, "x4plus", 0.0).image
    right = imaging.render_variant(master, variant("3x2"), 1.0, 0.5, "x4plus", 0.0).image
    assert np.asarray(left).mean() < 5          # focal at left: the strip is cropped away
    assert np.asarray(right).mean() > 10        # focal at right: it stays in


def test_dither_breaks_up_banding():
    smooth = np.tile(np.linspace(20.0, 24.0, 1000, dtype=np.float32), (512, 1))[:, :, None].repeat(3, axis=2)
    banded = np.rint(smooth).astype(np.uint8)
    dithered = imaging.tpdf_dither_quantize(smooth, 0.0, np.random.default_rng(1))
    banded_score, banded_tiles = imaging.banding_score(banded[:, :, 0])
    dithered_score, dithered_tiles = imaging.banding_score(dithered[:, :, 0])
    assert banded_tiles > 0 and banded_score == 1.0
    assert dithered_tiles > 0 and dithered_score < 0.3


def test_grain_changes_flat_areas_but_stays_small():
    flat = np.full((256, 256, 3), 100.0, dtype=np.float32)
    out = imaging.tpdf_dither_quantize(flat, 0.3, np.random.default_rng(3)).astype(int)
    assert out.std() > 0.3
    assert abs(out.mean() - 100) < 0.5


def test_encode_jpeg_is_444_progressive_with_srgb_profile():
    data = imaging.encode_jpeg(Image.new("RGB", (256, 144), (90, 120, 150)))
    with Image.open(io.BytesIO(data)) as im:
        assert im.format == "JPEG"
        assert im.info.get("progressive") or im.info.get("progression")
        assert im.info.get("icc_profile")
        assert im.layer[0][1:3] == (1, 1) if hasattr(im, "layer") else True  # no chroma subsampling


def test_thumbnail_is_640x360_even_for_portrait_sources():
    portrait = Image.new("RGB", (900, 1600), (10, 10, 10))
    thumb = imaging.make_thumbnail(imaging.thumbnail_source(portrait))
    assert imaging.sniff_dimensions(thumb) == (640, 360)
