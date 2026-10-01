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
