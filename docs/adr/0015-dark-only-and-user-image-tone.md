# 0015: Dark-only wallpapers and measured tone for user images

Date: 2026-10-09. Status: accepted.

Relates to SPEC FR-WP (rotation pools), FR-CON-7 (user images) and NFR-10 (the Defaults page is keyboard and screen-reader accessible).

## Context
People who want a calm, dark desktop asked for a way to see only dark wallpapers. The catalog already tags every wallpaper
`dark`, `mid` or `light`, and "follow Windows theme" already filters on that tone, but nothing lets a user keep light and
mid-tone art out of rotation for good. Separately, user images were all hard-coded to the `mid` tone, so a tone filter would
have silently removed every image the user had imported.

## Decision
1. **One setting:** `General.DarkWallpapersOnly` (default off). It is a single switch on the Defaults page, not a per-collection
   or per-game option, because "I only want dark wallpapers" is a preference about the whole desktop.
2. **One rule everywhere:** `ToneFilter.Allows(tone, darkOnly)` is used by the rotation pools (`ContextPlanner`, default and
   game), the fixed-wallpaper choice, collection counts and previews, game detail, library thumbnails and the picker. Light and
   mid-tone wallpapers are **hidden**, not disabled, so nothing shown on screen can be a light wallpaper.
3. **Honest empty state:** if the selection holds no dark wallpaper, Defaults says so and names a dark collection. The rotation
   shows its existing "no content" status rather than falling back to light art, which would break the promise of the setting.
   Counts are judged on the catalog, so a dark pack that is still downloading does not trigger the warning.
4. **Measured tone for user images:** on import, and once more for images imported before this change, the image's mean Rec. 709
   luma is sampled on a 64x36 grid: below 100 is dark, 160 and above is light, otherwise mid. The result is saved in a hidden
   `.tones` folder beside the images, keyed by the image's content-hash file name, so listing images on every rotation pass
   costs one small file read. A missing or garbled sidecar is measured again. An image that cannot be decoded stays mid-tone,
   so it is never shown as dark by mistake.

## Consequences
- Dark-only and "follow Windows theme" compose: with dark-only on, a light system theme still shows the dark pool (the theme
  fallback already keeps the desktop populated), because the user's explicit choice wins over the theme.
- Thresholds are heuristics. A bright photo with a dark frame can be classified as mid. The sidecar is a plain text file
  (`dark`, `mid` or `light`) that can be edited by hand to correct one. Revisit the thresholds if misclassifications are common.
- Catalog wallpapers keep their curated tone; only user images are measured.
