# Backlog (deferred work, with reasons)

The [next-update proposal](updates/2026-10-01-aesthetics-and-personalization.md) records
the owner's current requests and discussion choices. It is not implementation evidence.

| Item | Why deferred |
|---|---|
| Span-across-monitors mode (FR-MON-2, MAY) | v1.1 per spec |
| Riot / Battle.net / Xbox installed-game discovery (FR-DET-9, MAY) | Spec says "may come later" |
| Windows Spotlight compatibility | Automatic Spotlight resumption remains unsupported. New backups refuse to overwrite active Spotlight, preserving its behavior; the onboarding/Home text explains how to select Picture or Slideshow. Older Spotlight backups still restore the last picture with an explicit note. Slideshow source items, shuffle and interval now round-trip through documented COM APIs and a real desktop test. |
| Confirm `Lively.exe` is the right process name for Lively Wallpaper (FR-APPLY-7) | Not verifiable from the build environment; detection is warn-only, so a wrong name only means no warning. QA matrix item. |
| Confirm `BackgroundType` registry values (0 picture, 1 solid, 2 slideshow, 3 Spotlight) | Only community-documented (Microsoft Q&A). Unknown values are treated as "picture". QA matrix item. |
| Verify every seed game's exe name / AppID (docs/GAME_CATALOG_SEED.md) | Needs a real Windows machine with the games; use Settings → Advanced → Detection log, then set `verified` in `content/catalog.src.json`. |
| Public content deployment | A real P-256 public key is compiled into the app. Its private key is outside the checkout under `%LOCALAPPDATA%/PrettyDeskSigning`, encrypted with CurrentUser DPAPI. `tools/sign_content.ps1` signs and verifies staged catalogs. `tools/catalog_acceptance.ps1` passed over loopback HTTPS with valid, tampered and unsigned feeds. The actual public host still needs the owner's choice/authorization; no fictitious working URL has been inserted. |
| Lanczos3 resampling (FR-APPLY-2) | SkiaSharp has no Lanczos kernel; Mitchell + trilinear used instead (ADR 0003). |
| Remaining artwork and renditions | Generation remains incomplete. All 14 default collections, including the supplemental three-wallpaper Liquid Glass capsule, have offline starter coverage. Generated masters are saved incrementally under `art/raw`; `tools/review_generated.py`, `build_generated.py` and `assemble_content.py` preserve review and hash provenance. Check the dated `art/GENERATION_STATUS.json` report for actual coverage. Dedicated ultrawide and portrait masters remain a separate generation pass. |
| Content host (`ContentBaseUrl`) | OWNER-DECISION (SPEC 6.3): R2 bucket or a `prettydesk-content` GitHub repo. `contentBaseUrl` is empty in `content/catalog.src.json`; `assetpipe publish` refuses to run until it is an https URL. |
| 16-bit master decoding in `assetpipe` (SPEC 8 step 1) | Pillow cannot decode 16-bit RGB PNGs. ChatGPT exports 8-bit, and dithering happens at the final float-to-8-bit quantisation, which is where banding is prevented (ADR 0005). |
| Real-ESRGAN portability | The official Windows Vulkan release v0.2.5.0 (20220424 binary) was downloaded and exercised end to end on this Windows GPU machine for the starter set. Other GPU/driver combinations remain untested. |
| Verify Path of Exile 2 vs 1 exe names | Both ship `PathOfExile*.exe`. The seed rule uses `pathContains: ["Path of Exile 2"]` when the process path is readable. SPEC FR-DET-3 requires exe-only fallback when access is denied, so that fallback cannot disambiguate shared names. Validate both games on real machines and review that policy before marking either rule verified; do not request stronger process access. |
| NuGet lock portability | Lock files are enabled and generated on Windows: normal, RID-specific and ReadyToRun publish graphs have separate lock paths. Locked restores and self-contained ReadyToRun x64/ARM64 publishes pass locally. CI/release require locked restore; Linux CI execution of these new lock files is still required. |
| Project license | OWNER-DECISION (SPEC 13.4, free vs. monetized): no LICENSE file is added until the owner chooses one. Third-party notices are generated and shipped regardless. |
| Beta users also getting stable releases | Stable releases are packed for the `stable` channel only (ADR 0006); someone on beta gets betas until they switch the toggle back. Dual-publishing stable to the beta channel would double the artifacts. |
| Public code signing and full installer UI matrix | The real Velopack 1.0.0 → 1.0.1 test-feed update and uninstall pass on Windows x64; the hook restores both monitor image hashes, position and color and removes the Run key. The real native Yes/No dialog passed keep-data and delete-data tests against isolated settings and all five disposable data directories. ARM64 execution and Azure/OV Authenticode signing remain NOT RUN; the local packages are explicitly unsigned. |
| Wallpaper delivery and unavailable actions | Empty `contentBaseUrl` blocks remote game downloads in the EXE. Choose offline/online/hybrid delivery and expose clear availability/error states; do not bypass signing/hash checks. See the update proposal. |
| Active-session detection optimization | Proposed lightweight tracked-process checks, targeted foreground handling and slower fallback; preserve multi-game priority and exit grace, then measure real game frame times. |
| Layout, neutral themes and sidebar banner | Monitor preview adds a synthetic horizontal gap; pill alignment and visual direction need inspected light/dark WPF renders. Banner generation pending. |
| Recognizable Rocket League fan art | Owner approved named game/cars. Draft prompts are documented; scoped pack mode, lint/review migration, generation and public distribution review remain pending. |
| Taskbar appearance and desktop widgets | Separate optional capabilities, not static wallpaper features. Native settings guidance is feasible; custom shell glass and live surfaces require scoped ADRs, reversible lifecycle and compatibility/performance prototypes. |
| Broader top-100 game candidate catalog | Local game inventory comes first. Compile dated source/metric coverage without conflating Steam concurrency and broader monthly activity; verify every added rule on real installs. |

## Personalization update acceptance

See [the implementation matrix](updates/implementation-2026-10-01.md) and [ADR 0008](adr/0008-personalization-update.md). Native desktop surface acceptance, optimized native process liveness, remaining launcher providers/real launches, top-100 research, new banner and fan-art generation, public hosting and fresh package acceptance remain explicit work.
