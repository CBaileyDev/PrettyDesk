# PrettyDesk glass, Rocket League and responsiveness handoff

Status: **paused at the owner's request**, 2026-10-01. The goal is not complete.
Resume only when the owner requests it. This document records the current repair;
older review records and packages do not prove these changes shipped.

## Requested outcome

Deliver new recognizable Rocket League wallpapers, translucent app and taskbar
appearance, and responsive navigation with smooth transitions. Preserve Windows
controls, tray behavior, accessibility, native dialogs and baseline safety guards.
Start with [AGENTS](../AGENTS.md), [Development](DEVELOPMENT.md), and
[the repair decision](adr/0011-glass-and-responsive-navigation.md).

## Implemented in this checkout

- Four new generated car scenes: `rocket-league.octane-aerial`,
  `rocket-league.fennec-freeplay`, `rocket-league.batmobile-rain`, and
  `rocket-league.beach-aerial`. Masters are in `art/raw/game.rocket-league`;
  previous pilots were preserved in `TestResults/glass-repair/*_pilot-before.png`.
  Real-ESRGAN GPU upscaling produced reviewed 16:9, 16:10 and 3:2 variants.
  Dedicated ultrawide and portrait masters were NOT generated. The four scenes
  are approved in prompt YAML, assembled into the catalog/offline starter, and
  ordered first in the Rocket League pack. The starter is 109.80 MB within the
  recorded 128 MiB policy. Generated `art/PROMPTS.md` is refreshed; art README
  explains the coverage. Do not regenerate or discard existing art unnecessarily.
- `src/PrettyDesk.App/Services/AppAppearance.cs`: lighter translucent tints,
  frozen brushes, unchanged-palette suppression and coalesced material updates.
  Material application is deferred beyond Loaded. Synchronous WindowChrome
  changes caused an actual Freezable inheritance-context crash when Windows
  transparency was enabled; the deferred implementation passed subsequent tests.
- New `Views/MotionContentControl.cs` lazily retains each page visual in a Grid,
  collapses inactive pages, and uses 160 ms opacity/translation transitions.
  High contrast and disabled Windows animations skip transitions. MainWindow
  releases pages and the thumbnail cache on close. MainWindow XAML uses this host.
- `Converters/Converters.cs`: 16 MiB bounded decoded-thumbnail cache, keyed by
  path, timestamp, size and decode width. Stream decoding closes file handles;
  replacements invalidate cached images. This is not asynchronous first decoding.
- `Views/LibraryPage.xaml`: existing WPF-UI VirtualizingGridView replaces the
  eager WrapPanel; recycled visible cards retain the existing card design.
- `tools/assetpipe/assetpipe/commands.py`: raw hash updates now replace old YAML
  block-map children as well as the header. Regression coverage prevents the
  invalid YAML observed during this repair. Affected prompt YAML was repaired.
- `tests/PrettyDesk.App.Tests/ImageCacheTests.cs` exercises decoding, replacement,
  reuse and clearing. AppSmokeTests verifies the four offline Rocket League
  assets, measures optional navigation layout timing, and verifies virtualized
  last-item scrolling and page-instance/scroll preservation after navigation.

## Machine state and taskbar limitation

Windows Colors > Transparency effects was Off and was turned On through Windows
Settings for the explicit glass request. Original value 0 was saved in
`TestResults/glass-repair/transparency-before.json`. Reversal is the same Windows
toggle; do not silently revert the user's requested appearance or repeatedly
rewrite the preference. No accent change or custom taskbar utility was installed.

Only **native Windows taskbar translucency** has been enabled. A stronger custom
clear/tinted taskbar is NOT implemented, and the actual taskbar result has NOT
been visually verified. Do not claim the taskbar request is fully satisfied.
Explorer injection/hooks remain prohibited by repository constraints. App acrylic
also still needs final live visual acceptance; offscreen WPF captures cannot prove
DWM translucency. The user's existing Spotlight guard remains intact.

The manually launched development app was stopped before the final build to
release its files. The final test process exited successfully. Windows Settings
may remain open. No fresh package was created for this repair. No Git metadata
was present; do not initialize a repository or assume a clean baseline.

## Validation completed

| Check | Result and scope |
|---|---|
| Release solution build with locked restore | Passed, zero warnings/errors on the successful final build |
| Core tests | 496 passed, 1 skipped, 497 total; live HTTPS fixture skipped |
| Presentation tests | 236 passed |
| App tests, latest run | 9 passed, including cache, offline artwork and Library virtualization/navigation checks |
| Python pipeline tests | 15 passed |
| Catalog validator | Passed |
| Starter verification | 232 catalog assets verified, 109.80 MB bundle |
| Prompt generation/check | Passed, 59 packs / 293 wallpapers / zero warnings |
| Documentation check | Passed after this handoff and new ADR: 37 documents, 266 local links, zero errors |
| Format verification | Earlier run passed; rerun after the latest virtualization/test edits |

An earlier build encountered a file lock from the development app; that process
was stopped and the subsequent build passed. Do not treat the recovered failure
as an unresolved compiler issue.

Latest `TestResults/glass-repair/ui/navigation-timing.json`: repeated page
switches took **1.51–5.60 ms of UI/layout work** in that sample. First visits took
Library 535.53 ms, Defaults 231.06 ms, Settings 156.50 ms, About 51.53 ms and Home
6.34 ms. The earlier baseline in
`TestResults/glass-repair/navigation-before-page-cache.json` measured repeated
Library visits around 712–905 ms. These are bounded local measurements, not a
frame-rate guarantee, DWM presentation timing, game performance or a soak result.

Screenshots and timing receipts are under `TestResults/glass-repair/ui`; the
Library capture shows the new Octane cover and Ready availability. Some captures
use a light offscreen backing and must not be used to assert glass/dark rendering.

## Remaining work when resumed

1. Run documentation and formatting checks for the final source state. Inspect
   the new navigation host, cache and virtualization behavior for lifecycle,
   keyboard and accessibility regressions. Avoid unneeded dependency changes.
2. Launch the current app and inspect actual app acrylic in light/dark, navigation,
   scrolling and the four Rocket League entries. Verify native taskbar appearance
   directly. Keep custom taskbar limitations explicit; do not introduce Explorer
   injection, foreign-window hooks or a silent companion install.
3. Check high contrast, reduced motion, transparency-off fallback, keyboard and
   relevant DPI behavior. These broader native checks are NOT RUN for this repair.
   Cold initialization remains measurable; investigate only with evidence if it
   still makes the experience unsatisfactory.
4. Produce a fresh local package only after remaining checks. Existing release
   folders reach beta.7; the planned next candidate is beta.8. Recheck collision
   before running `tools/package.ps1 -Version 1.0.0-beta.8 -Runtime win-x64`.
   Preserve all previous installers and output. Validate the actual candidate,
   including bundled artwork; a test-build screenshot is not package acceptance.
5. Write dated acceptance evidence and reconcile stale backlog/proposal rows:
   some still say fan-art generation is pending. Preserve original dated records
   and distinguish newly completed landscape artwork from remaining U/P coverage.

Windows native suite, fresh packaging/install/update/uninstall, final DWM/taskbar
inspection, broad accessibility/DPI matrix, game recognition/live game operation,
and performance soak were NOT RUN as final acceptance for this repair. Public
signing/hosting and fan-art distribution-rights review remain separate release
gates. Do not mark the goal complete just because automated tests pass.

## Tool and asset continuation notes

Use `tools/.venv/Scripts/python.exe` for Python. The installed GPU upscaler is in
`tools/bin/realesrgan`; prepend that directory to the task process PATH before
assetpipe builds, otherwise it can fall back to plain scaling. Current variants
already built successfully with the GPU path, so no rebuild is needed by default.
Use prompt YAML as source, never hand-edit generated `art/PROMPTS.md` or invent
hash metadata. `TestResults/glass-repair/reviewed.json` records the four IDs.

Native inspection used the computer-use skill and `@oai/sky` through the Node REPL.
Read the skill before continuing; refresh window discovery and accessibility
state instead of reusing old handles/indices. Browser-only cua has native apps
disabled. No screenshot of the taskbar was obtained. Do not fabricate native proof
from offscreen captures or stale snapshots.
