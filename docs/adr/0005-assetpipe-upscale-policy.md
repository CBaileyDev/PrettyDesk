# 0005. Asset pipeline: when to run the AI upscaler

- Status: accepted
- Relates to: SPEC §8 (build steps 1-3), ART_DIRECTION §6

## Context
SPEC §8 says to upscale 4x with Real-ESRGAN and then downscale "when the source is smaller than the target" and to skip the
upscale when the source is already large enough. Taken literally, almost every wallpaper needs it: a 16:9 master of exactly
3840x2160 has to be cropped to 3840x2400's ratio for the `16x10` variant (3456x2160 of real pixels) and the `3x2` variant, and
ChatGPT masters are typically about 2K, not 4K. Running a 4x GPU model for a x1.11 enlargement is slow and gains nothing the Lanczos
resize that follows does not already give, and the model can add its own texture to flat gradients.

## Decision
- Compute the **enlargement factor** of the focal crop to the canonical variant size.
- Up to **x1.25** the final Lanczos resize does the enlargement (in float, before dithering).
- Above x1.25 the **AI model runs once** (4x, model chosen by the wallpaper's `upscaler`), and the result is Lanczos-resized down to the target.
- When the model binary (`realesrgan-ncnn-vulkan`) is not installed, the build **continues with a plain Lanczos enlargement**
  and prints a warning that the result will look soft, so a contributor without a GPU can still preview the whole flow.

## Consequences
- `16x10` and `3x2` from a 4K `L` master never invoke the model. A 2K master (the common case) uses it for every variant, as intended.
- The threshold lives in `assetpipe/variants.py` (`AI_UPSCALE_THRESHOLD`) with tests that pin the behavior.
- Pillow cannot decode 16-bit RGB PNGs, so masters are processed as 8-bit sRGB (what ChatGPT exports). Dithering happens at the final
  quantisation from float, which is where banding is actually prevented. Logged in `docs/BACKLOG.md`.
