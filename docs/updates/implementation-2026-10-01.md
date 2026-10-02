# Personalization implementation and acceptance

This records work for the owner-requested code-first implementation. It does not replace prior dated evidence.

| Plan item | Code / current state | Outstanding acceptance |
|---|---|---|
| Wallpaper delivery | Hybrid policy; seven game packs offline; unavailable state, disabled download, explicit retry; fresh unsigned beta.4 package | Real installer/portable wallpaper matrix, production host |
| Detection cadence | Unchanged-session matching reuse with baseline snapshots | Native tracked-process liveness optimization, CPU and game frame-time comparison, long soak |
| Installed discovery | Steam/Epic plus bounded declared-install roots with executable evidence | Xbox manifest provider, real launch identifiers for missing local titles, provider coverage |
| Monitor layout | Common coordinate transform; readable labels outside preview | Mixed DPI and hardware matrix |
| Themes | Persisted System/Light/Dark and neutral palettes | Live OS theme/high-contrast/DPI keyboard matrix |
| Banner | Centered horizontal vector monitor mark and PrettyDesk wordmark, transparent background; inspected in light/dark WPF renders | Hardware scaling matrix |
| Rocket League | Scoped named-art pipeline; four new unapproved pilot IDs; existing art retained | Four landscapes generated and inspected; regenerate failed composition/fidelity cases, then companion crop review before integration |
| Taskbar | Explicit Windows Colors guidance | Experimental shell customization remains unselected |
| Clock/music | Opt-in own-window prototype, now-playing controls and WASAPI visualizer | Native focus/z-order/device/Explorer/DPI and CPU/GPU verification |
| Top-100 candidates | Existing catalog retained | Dated primary-source research dataset and real detection validation |

See [ADR 0008](../adr/0008-personalization-update.md) for scope and choices. Do not infer release readiness from compilation.

Current checks: zero-warning locked build; Core 489 passed/1 skipped and Presentation 230 passed; 72 Python assetpipe tests; isolated WPF smoke passed with clock create/disable, no focus activation or binding errors. The latest settings, monitor layout, light/dark main window and clock captures were inspected under `TestResults/personalization-code/ui`. Formatting, resource generation, third-party notices, catalog schema, prompt drift and dependency vulnerability checks passed. The offline bundle verified 224 assets at 100.69 MiB.

Latest x64 and ARM64 locked self-contained ReadyToRun publishes passed. The unsigned x64 installer and portable package are in `dist/releases/1.0.0-beta.4/win-x64`, with generated SHA256 hashes. Packaging did not install the application. ARM64 publish success is not ARM64 hardware proof.

Full Windows/App suites are pending because the existing PrettyDesk instance is running. No process was stopped without an answer to the pending user preference. The Core skip is the separate signed HTTPS fixture; it has not been run for this change. Live audio/device, game recognition, performance, install/update/uninstall and full native widget behavior remain unverified. The remaining code and research items in the table are incomplete; this is an implementation pass, not completion of the entire proposal.

## Display-matched onboarding follow-up

[ADR 0009](../adr/0009-display-matched-onboarding.md) records the added wallpaper
step, display-specific missing-file preview and persistent per-game prefetch choice.
Two 1440p landscape displays request one 16:9 file per wallpaper, plus thumbnails;
mixed setups request the union of their formats. Already bundled/current local
files are excluded from estimates. New formats requested during an existing
download are fetched afterward. Scan failure, missing displays, unavailable
hosting, progress and retry are explicit states. Downloads do not block setup;
the global background choice and per-game preferences are saved, with the latter
editable in Library details. Original user settings are not changed by test fixtures.

Validation: locked Release build with zero warnings/errors; Core 496 passed and
one signed-HTTPS fixture skipped; Presentation 236 passed. Isolated WPF smoke
passed without binding errors. Actual 760×600 and 640×520 onboarding renders,
plus downloading, failed, offline and empty states, were inspected under
`TestResults/onboarding-downloads/ui`. The download controls stay visible while
the game list scrolls. Formatting, generated strings and documentation checks pass.

The new unsigned x64 package is `dist/releases/1.0.0-beta.5/win-x64`; creation is
separate from installation acceptance. Production-host acquisition, physical
display hot-plug/DPI/keyboard/high-contrast testing and full wallpaper-changing
Windows/App suites are not run for this follow-up.

## Brand header follow-up

Replace the decorative sidebar banner with a centered horizontal PrettyDesk
wordmark and original vector monitor/landscape mark. It has no background tile,
uses live theme/accessibility brushes and scales as WPF geometry/text. Retain the
earlier generated banner files as provenance, but exclude them from app resources.
The isolated WPF smoke passed; light/dark and minimum-window captures are under
`TestResults/brand-logo/ui`. The exported light/dark logo PNGs have alpha-zero
background pixels. The locked build has zero warnings/errors. The unsigned x64
package for this revision is `dist/releases/1.0.0-beta.6/win-x64`; it has not been
installed over the running user instance.

The four named Rocket League landscape drafts remain in `art/raw/game.rocket-league`
under their pilot IDs. They are unapproved and excluded from the catalog/bundle;
the existing approved generic wallpapers are still shipped. Fidelity/composition
correction and companion crop review remain outstanding.

## Hazy window refinement

[ADR 0010](../adr/0010-hazy-window-material.md) records the own-window Acrylic
integration, static optical tints and opaque accessibility fallback. The redundant
visible title-bar text is removed; native controls and the actual window title are
retained. Home cards have more vertical padding and Library/collection artwork is
24 pixels taller. Denser content cards sit over softer shell surfaces.

The locked build has zero warnings/errors. Isolated WPF smoke passed with no
binding errors; actual light/dark and minimum-size captures were inspected under
`TestResults/hazy-glass/ui`. Windows transparency is disabled on this machine and
was not changed, so these renders show the opaque tinted fallback. Live native
blur, transparency-enabled contrast, GPU/performance, mixed DPI and high-contrast
verification remain unrun. The updated unsigned x64 package is under
`dist/releases/1.0.0-beta.7/win-x64`; no installation or user-instance restart is
performed by package creation.
