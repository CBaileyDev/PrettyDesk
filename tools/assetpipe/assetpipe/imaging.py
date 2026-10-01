"""Pixel work: load, colour-manage, (optionally) upscale, focal-crop, resize, dither, grain, encode (SPEC section 8)."""

from __future__ import annotations

import io
import shutil
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageCms

from .variants import MIN_MASTER_LONG_EDGE, THUMB_SIZE, CropRect, Variant, enlarge_factor, focal_crop, needs_upscale

Image.MAX_IMAGE_PIXELS = 400_000_000  # our own masters; the default guard is for untrusted input

_SRGB = ImageCms.createProfile("sRGB")
SRGB_ICC = ImageCms.ImageCmsProfile(_SRGB).tobytes()


class ImageProblem(Exception):
    pass


def load_master(path: Path) -> Image.Image:
    """Open a master as 8-bit sRGB RGB. Pillow cannot decode 16-bit RGB, so ChatGPT's 8-bit PNGs are the supported input."""
    try:
        with Image.open(path) as im:
            im.load()
            icc = im.info.get("icc_profile")
            rgb = im.convert("RGB") if im.mode != "RGB" else im.copy()
            if icc:
                try:
                    src = ImageCms.ImageCmsProfile(io.BytesIO(icc))
                    rgb = ImageCms.profileToProfile(rgb, src, _SRGB, outputMode="RGB")
                except ImageCms.PyCMSError:
                    pass  # unreadable profile: treat as sRGB, which is what ChatGPT exports
            return rgb
    except (OSError, ValueError) as ex:
        raise ImageProblem(f"cannot decode {path.name}: {ex}") from ex


def check_master(img: Image.Image, kind: str) -> list[str]:
    """Problems that make a master unusable. ``kind`` is L, U or P."""
    w, h = img.size
    problems: list[str] = []
    if max(w, h) < MIN_MASTER_LONG_EDGE:
        problems.append(f"{w}x{h} is below the {MIN_MASTER_LONG_EDGE}px long-edge minimum")
    ratio = w / h
    if kind == "L" and not (1.5 <= ratio <= 2.0):
        problems.append(f"landscape master ratio {ratio:.2f} is not close to 16:9")
    if kind == "U" and ratio < 2.3:
        problems.append(f"ultrawide master ratio {ratio:.2f} is narrower than 21:9 (2.33)")
    if kind == "P" and not (0.5 <= ratio <= 0.65):
        problems.append(f"portrait master ratio {ratio:.2f} is not close to 9:16")
    return problems


def realesrgan_path() -> str | None:
    return shutil.which("realesrgan-ncnn-vulkan")


def upscale_4x(img: Image.Image, model: str) -> Image.Image:
    """4x upscale through realesrgan-ncnn-vulkan. Callers check ``realesrgan_path()`` first."""
    exe = realesrgan_path()
    if exe is None:
        raise ImageProblem("realesrgan-ncnn-vulkan is not on PATH")
    with tempfile.TemporaryDirectory() as tmp:
        src, dst = Path(tmp) / "in.png", Path(tmp) / "out.png"
        img.save(src)
        subprocess.run([exe, "-i", str(src), "-o", str(dst), "-n", f"realesrgan-{model}", "-s", "4"], check=True, capture_output=True)
        with Image.open(dst) as out:
            return out.convert("RGB")


def _resize_float(arr: np.ndarray, size: tuple[int, int]) -> np.ndarray:
    """Lanczos resize per channel in float so dither can act on sub-LSB values (mode 'F')."""
    channels = []
    for c in range(arr.shape[2]):
        plane = Image.fromarray(arr[:, :, c].astype(np.float32), mode="F")
        channels.append(np.asarray(plane.resize(size, Image.LANCZOS), dtype=np.float32))
    return np.stack(channels, axis=2)


def tpdf_dither_quantize(arr: np.ndarray, grain: float, rng: np.random.Generator) -> np.ndarray:
    """Quantise float 0..255 data to uint8 with triangular-PDF dither and optional luminance grain (SPEC 8 step 4)."""
    noise = rng.random(arr.shape, dtype=np.float32) - rng.random(arr.shape, dtype=np.float32)  # TPDF in (-1, 1)
    out = arr + noise
    if grain > 0:
        luma_noise = rng.standard_normal(arr.shape[:2], dtype=np.float32) * (grain * 2.0)
        out = out + luma_noise[:, :, None]
    return np.clip(np.rint(out), 0, 255).astype(np.uint8)


@dataclass(frozen=True)
class RenderResult:
    image: Image.Image
    crop: CropRect
    upscaled: bool          # the AI model ran
    enlarge: float          # factor the final Lanczos resize had to enlarge by (1.0 = none)
    soft: bool              # a big deficit that could not use the AI model, so the result will look soft


def render_variant(master: Image.Image, spec: Variant, fx: float, fy: float, model: str, grain: float, seed: int = 0) -> RenderResult:
    crop = focal_crop(master.width, master.height, spec.ratio, fx, fy)
    working = master
    upscaled = False
    soft = False
    if needs_upscale(crop, spec.width, spec.height):
        if realesrgan_path() is not None:
            working = upscale_4x(master, model)
            upscaled = True
            crop = focal_crop(working.width, working.height, spec.ratio, fx, fy)
        else:
            soft = True
    box = (round(crop.x), round(crop.y), round(crop.x + crop.width), round(crop.y + crop.height))
    cropped = np.asarray(working.crop(box), dtype=np.float32)
    resized = _resize_float(cropped, (spec.width, spec.height))
    out = tpdf_dither_quantize(resized, grain, np.random.default_rng(seed))
    return RenderResult(Image.fromarray(out, mode="RGB"), crop, upscaled, enlarge_factor(crop, spec.width, spec.height), soft)


def encode_jpeg(img: Image.Image, quality: int = 95) -> bytes:
    buffer = io.BytesIO()
    img.save(buffer, "JPEG", quality=quality, subsampling=0, progressive=True, optimize=True, icc_profile=SRGB_ICC)
    return buffer.getvalue()


def make_thumbnail(img: Image.Image) -> bytes:
    thumb = img.resize(THUMB_SIZE, Image.LANCZOS) if img.size != THUMB_SIZE else img
    buffer = io.BytesIO()
    thumb.save(buffer, "JPEG", quality=85, subsampling=0, progressive=True, optimize=True, icc_profile=SRGB_ICC)
    return buffer.getvalue()


def thumbnail_source(img: Image.Image) -> Image.Image:
    """Centre-crop to 16:9 so portrait variants still yield a 640x360 thumb."""
    target = THUMB_SIZE[0] / THUMB_SIZE[1]
    if abs(img.width / img.height - target) < 0.01:
        return img
    crop = focal_crop(img.width, img.height, target, 0.5, 0.5)
    return img.crop((round(crop.x), round(crop.y), round(crop.x + crop.width), round(crop.y + crop.height)))


def banding_score(luma: np.ndarray, tile: int = 64) -> tuple[float, int]:
    """Fraction of gentle-gradient tiles that show flat plateaus (visible steps), and how many tiles were judged.

    A tile is a gentle gradient when its levels span 1..12 (flat tiles are legitimately flat; busy tiles are texture).
    A properly dithered gradient flips neighbouring pixels between adjacent levels, so few neighbours are equal; a banded
    one has long runs of identical values, so almost all are.
    """
    h, w = luma.shape
    th, tw = h // tile, w // tile
    if th == 0 or tw == 0:
        return 0.0, 0
    tiles = luma[: th * tile, : tw * tile].reshape(th, tile, tw, tile).swapaxes(1, 2).reshape(-1, tile, tile).astype(np.int16)
    flat = tiles.reshape(len(tiles), -1)
    spread = flat.max(axis=1) - flat.min(axis=1)
    judged = np.flatnonzero((spread >= 1) & (spread <= 12))
    if len(judged) == 0:
        return 0.0, 0
    equal = (tiles[judged][:, :, 1:] == tiles[judged][:, :, :-1]).mean(axis=(1, 2))
    return float((equal > 0.9).sum()) / len(judged), len(judged)


def luma_of(img: Image.Image) -> np.ndarray:
    return np.asarray(img.convert("L"))


def sniff_dimensions(data: bytes) -> tuple[int, int]:
    with Image.open(io.BytesIO(data)) as im:
        return im.size
