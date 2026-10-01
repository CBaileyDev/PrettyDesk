"""Variant table and focal-crop math.

Mirrors ``PrettyDesk.Core.Imaging.Variants`` and ``FocalCropper`` (SPEC 6.1) so that what the pipeline
produces is exactly what the app expects. Keep the two in sync; ``tests/test_variants.py`` pins the numbers.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Variant:
    key: str
    width: int
    height: int
    source: str  # which master it is derived from: L, U or P

    @property
    def ratio(self) -> float:
        return self.width / self.height


VARIANTS: tuple[Variant, ...] = (
    Variant("16x9", 3840, 2160, "L"),
    Variant("16x10", 3840, 2400, "L"),
    Variant("3x2", 3000, 2000, "L"),
    Variant("21x9", 5120, 2160, "U"),
    Variant("32x9", 5120, 1440, "U"),
    Variant("9x16", 2160, 3840, "P"),
)

THUMB_SIZE = (640, 360)
MIN_MASTER_LONG_EDGE = 1536  # SPEC 8: accept anything >= 1536 px on the long edge
MAX_FILE_BYTES = 8 * 1024 * 1024


def variants_for_source(source: str) -> list[Variant]:
    return [v for v in VARIANTS if v.source == source]


def variant(key: str) -> Variant:
    for v in VARIANTS:
        if v.key == key:
            return v
    raise KeyError(key)


@dataclass(frozen=True)
class CropRect:
    x: float
    y: float
    width: float
    height: float


def focal_crop(src_w: int, src_h: int, target_ratio: float, fx: float, fy: float) -> CropRect:
    """Largest rectangle of ``target_ratio`` inside the source, centred on the focal point and clamped to the image."""
    src_ratio = src_w / src_h
    w = float(src_w)
    h = float(src_h)
    if src_ratio > target_ratio:
        w = src_h * target_ratio
    elif src_ratio < target_ratio:
        h = src_w / target_ratio

    cx = min(max(fx, 0.0), 1.0) * src_w
    cy = min(max(fy, 0.0), 1.0) * src_h
    x = min(max(cx - w / 2, 0.0), src_w - w)
    y = min(max(cy - h / 2, 0.0), src_h - h)
    return CropRect(x, y, w, h)


# Enlarging by up to this factor is done by the Lanczos resize that produces the variant anyway; AI upscaling is
# reserved for real deficits. A 16:9 master at 3840x2160 needs only x1.11 to fill a 16x10 variant, which Lanczos handles
# cleanly, whereas a 1536 px master needs x2.5 and gets the 4x model (docs/adr/0005-assetpipe-upscale-policy.md).
AI_UPSCALE_THRESHOLD = 1.25


def enlarge_factor(crop: CropRect, target_w: int, target_h: int) -> float:
    """How much the cropped region must be enlarged to reach the target (1.0 or less means it is large enough)."""
    return max(target_w / crop.width, target_h / crop.height, 1.0)


def needs_upscale(crop: CropRect, target_w: int, target_h: int) -> bool:
    """True when the crop is so much smaller than the output that the AI upscaler should run before the final resize."""
    return enlarge_factor(crop, target_w, target_h) > AI_UPSCALE_THRESHOLD


def best_variant_key(monitor_ratio: float, available: list[str]) -> str:
    """Smallest |ln(monitor) - ln(variant)|, the same rule as the app's VariantSelector."""
    import math

    best = None
    best_delta = math.inf
    for key in available:
        v = variant(key)
        delta = abs(math.log(monitor_ratio) - math.log(v.ratio))
        if delta < best_delta:
            best, best_delta = key, delta
    if best is None:
        raise ValueError("no variants available")
    return best
