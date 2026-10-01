# Backlog (deferred work, with reasons)

| Item | Why deferred |
|---|---|
| Span-across-monitors mode (FR-MON-2, MAY) | v1.1 per spec |
| Riot / Battle.net / Xbox installed-game discovery (FR-DET-9, MAY) | Spec says "may come later" |
| Restoring a **slideshow** or **Windows Spotlight** background on restore/uninstall | No documented API turns Spotlight back on, and slideshow restore needs the original folder list. v1 restores the last picture and tells the user (see `WallpaperBackupService.RestoreAsync`). |
| Confirm `Lively.exe` is the right process name for Lively Wallpaper (FR-APPLY-7) | Not verifiable from the build environment; detection is warn-only, so a wrong name only means no warning. QA matrix item. |
| Confirm `BackgroundType` registry values (0 picture, 1 solid, 2 slideshow, 3 Spotlight) | Only community-documented (Microsoft Q&A). Unknown values are treated as "picture". QA matrix item. |
| Verify every seed game's exe name / AppID (docs/GAME_CATALOG_SEED.md) | Needs a real Windows machine with the games; use Settings → Advanced → Detection log, then set `verified` in `content/catalog.src.json`. |
| Production catalog signing key (`TrustedKeys.PublicKeysBase64`) | OWNER-DECISION: generate with `catalog-sign keygen`; until then remote catalogs are rejected (fail closed) and the app runs on its bundled snapshot. |
| Lanczos3 resampling (FR-APPLY-2) | SkiaSharp has no Lanczos kernel; Mitchell + trilinear used instead (ADR 0003). |
| Artwork itself (106 default + 180 game wallpapers) | OWNER task: prompts are done (`art/PROMPTS.md`); images come from ChatGPT by hand. Until `assetpipe build/publish/starter` have run, the bundled catalog lists every game and collection but no wallpapers, and the app shows its designed empty states ("no wallpapers yet"). |
| Content host (`ContentBaseUrl`) | OWNER-DECISION (SPEC 6.3): R2 bucket or a `prettydesk-content` GitHub repo. `contentBaseUrl` is empty in `content/catalog.src.json`; `assetpipe publish` refuses to run until it is an https URL. |
| 16-bit master decoding in `assetpipe` (SPEC 8 step 1) | Pillow cannot decode 16-bit RGB PNGs. ChatGPT exports 8-bit, and dithering happens at the final float-to-8-bit quantisation, which is where banding is prevented (ADR 0005). |
| Real-ESRGAN not exercised end to end | `realesrgan-ncnn-vulkan` is not available in the build environment; the call path is covered by a stub test only. Owner to run one real build on a GPU machine. |
| Verify Path of Exile 2 vs 1 exe names | Both ship `PathOfExile*.exe`. The seed rule uses `pathContains: ["Path of Exile 2"]` so PoE1 does not trigger PoE2 art; if the install folder differs, the rule fails safe (no wallpaper) until corrected in the catalog. |
| Interim procedural starter wallpapers | Not built: real art from the owner replaces the need, and placeholder images presented as collection art would be misleading (CLAUDE.md "no fake data"). |
