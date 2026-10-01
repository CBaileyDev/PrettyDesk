import math

import pytest

from assetpipe.variants import VARIANTS, best_variant_key, enlarge_factor, focal_crop, needs_upscale, variant, variants_for_source


def test_canonical_sizes_match_the_spec_table():
    sizes = {v.key: (v.width, v.height) for v in VARIANTS}
    assert sizes == {
        "16x9": (3840, 2160), "16x10": (3840, 2400), "3x2": (3000, 2000),
        "21x9": (5120, 2160), "32x9": (5120, 1440), "9x16": (2160, 3840),
    }


def test_sources_split_between_masters():
    assert [v.key for v in variants_for_source("L")] == ["16x9", "16x10", "3x2"]
    assert [v.key for v in variants_for_source("U")] == ["21x9", "32x9"]
    assert [v.key for v in variants_for_source("P")] == ["9x16"]


def test_focal_crop_full_frame_when_ratio_matches():
    crop = focal_crop(3840, 2160, 16 / 9, 0.3, 0.3)
    assert (crop.x, crop.y, crop.width, crop.height) == (0, 0, 3840, 2160)


def test_focal_crop_16x9_to_3x2_removes_about_16_percent_width():
    crop = focal_crop(3840, 2160, 1.5, 0.5, 0.5)
    assert crop.height == 2160
    assert crop.width == pytest.approx(3240)
    assert 1 - crop.width / 3840 == pytest.approx(0.156, abs=0.001)
    assert crop.x == pytest.approx(300)


def test_focal_crop_follows_the_focal_point_and_clamps_to_the_image():
    right = focal_crop(3840, 2160, 1.5, 0.95, 0.5)
    assert right.x + right.width == pytest.approx(3840)
    left = focal_crop(3840, 2160, 1.5, 0.0, 0.5)
    assert left.x == 0


def test_focal_crop_ultrawide_master_to_21x9_trims_width_and_to_32x9_trims_height():
    to_21 = focal_crop(5760, 1920, 5120 / 2160, 0.5, 0.5)
    assert to_21.height == 1920 and to_21.width == pytest.approx(1920 * 5120 / 2160)
    to_32 = focal_crop(5760, 1920, 5120 / 1440, 0.5, 0.5)
    assert to_32.width == 5760 and to_32.height == pytest.approx(5760 / (5120 / 1440))
    assert 1 - to_32.height / 1920 == pytest.approx(0.156, abs=0.01)


def test_focal_crop_clamps_out_of_range_focal_points():
    crop = focal_crop(1000, 1000, 2.0, 5.0, -3.0)
    assert crop.y == 0 and crop.x == 0 and crop.width == 1000


def test_ai_upscale_only_for_real_deficits():
    spec = variant("16x9")
    assert not needs_upscale(focal_crop(3840, 2160, spec.ratio, 0.5, 0.5), spec.width, spec.height)
    assert not needs_upscale(focal_crop(3200, 1800, spec.ratio, 0.5, 0.5), spec.width, spec.height)  # x1.2: Lanczos is fine
    assert needs_upscale(focal_crop(2560, 1440, spec.ratio, 0.5, 0.5), spec.width, spec.height)       # x1.5: use the model


def test_a_16x9_master_needs_only_a_small_enlargement_for_16x10():
    spec = variant("16x10")
    crop = focal_crop(3840, 2160, spec.ratio, 0.5, 0.5)
    assert enlarge_factor(crop, spec.width, spec.height) == pytest.approx(2400 / 2160)
    assert not needs_upscale(crop, spec.width, spec.height)


@pytest.mark.parametrize(
    ("ratio", "expected"),
    [(1920 / 1080, "16x9"), (1920 / 1200, "16x10"), (3000 / 2000, "3x2"), (3440 / 1440, "21x9"), (5120 / 1440, "32x9"), (1080 / 1920, "9x16")],
)
def test_best_variant_key_picks_the_nearest_ratio(ratio, expected):
    assert best_variant_key(ratio, [v.key for v in VARIANTS]) == expected


def test_best_variant_key_falls_back_to_the_closest_available():
    assert best_variant_key(5120 / 1440, ["16x9", "21x9"]) == "21x9"
    with pytest.raises(ValueError):
        best_variant_key(1.6, [])


def test_variant_ratio_is_width_over_height():
    assert variant("21x9").ratio == pytest.approx(5120 / 2160)
    assert math.isclose(variant("9x16").ratio, 0.5625)
