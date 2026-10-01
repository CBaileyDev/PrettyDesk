# 0003 — Resampling filter: Mitchell cubic + trilinear instead of Lanczos3

**Status:** accepted · **Date:** 2026-10-01 · **Relates to:** SPEC FR-APPLY-2, §5.1 (SkiaSharp)

## Context
FR-APPLY-2 asks for "a high-quality filter (Lanczos3, or Mitchell for downscale)". SkiaSharp exposes cubic resamplers
(Mitchell, Catmull-Rom) plus linear/mip-mapped filtering, but **no Lanczos kernel**.

## Decision
- Scale ≥ 0.9 (up-scaling or near 1:1, e.g. 3840×2160 → 3840×2160 or 2560×1440 from a 3840 source cropped narrower): **Mitchell cubic**.
- Scale < 0.9 (real down-scaling, the common 4K-variant → 1080p/1440p case): **trilinear with mip-maps**, which pre-filters
  and avoids the aliasing a bare cubic produces on large reductions.
- Output is always an exact-size sRGB PNG.

## Consequences
Sharpness is marginally softer than Lanczos3 on huge reductions; compensated by the asset pipeline shipping 4K masters
(so on-device down-scales are ≤ 2×). If Lanczos matters later, the renderer can switch to a custom
`Pillow`-equivalent kernel behind the same `RenderToFile` seam.
