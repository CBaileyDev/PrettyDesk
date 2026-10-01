# Performance and reliability record (SPEC NFRs)

This file records how each non-functional requirement is **measured** and what has actually been **measured so far**.
A row stays "not measured yet" until a person has run it on real Windows hardware; nothing here is estimated.

| NFR | Target | How to measure | Status |
|---|---|---|---|
| NFR-1 Idle CPU (window closed, 10 min, 4-core laptop) | < 0.2 % average, no sustained spikes | `tools/perf/soak.ps1` (default 10 min) | **not measured yet** |
| NFR-2 Idle memory (window closed) and 24 h growth | < 80 MB private bytes, no growth over 24 h | `tools/perf/soak.ps1 -Hours 24`; compare first and last 10 % of samples | **not measured yet** |
| NFR-3 Game start to wallpaper (pack cached) | <= `detectDelay` + 1.5 s | Launch a game, time from process start to wallpaper change (screen recording at 60 fps); repeat 5x | **not measured yet** |
| NFR-4 Game exit to default | <= `exitGrace` + 1.5 s | Same, closing the game | **not measured yet** |
| NFR-5 Cold start to tray icon | < 1.5 s on an SSD | Reboot-free: end task, launch, stopwatch to tray icon; 5 runs, report the median | **not measured yet** |
| NFR-6 Permissions | no admin, per-user, no services/drivers/tasks | Enforced by `app.manifest` (`asInvoker`), source guard tests, and the QA install check | **enforced in code and tests**; install check is on the QA list |
| NFR-7 Offline | fully functional with the bundled starter set | Block the network, start the app, switch games and rotate | covered by unit tests (`CatalogService`, `ContentLibrary`); end-to-end on the QA list |
| NFR-8 Platforms | win-x64 and win-arm64 native | CI builds both RIDs with zero warnings (`windows-latest`); arm64 run is on the QA list | **builds proven by CI**, arm64 run not yet done |
| NFR-9 Per-Monitor-V2 DPI | correct at 100-300 % | `app.manifest` declares it; verify on the QA display matrix | declared; visual check on the QA list |
| NFR-10 Accessibility | keyboard, focus, AutomationProperties, AA contrast | A static test checks every bound control; a Windows UI test instantiates every page with zero WPF binding errors (CI) | **partially proven**; screen reader pass is on the QA list |
| NFR-11 Localization-ready | all strings in `.resx` | `python3 tools/strings.py check` in CI | **proven by CI** |
| NFR-12 Privacy | no telemetry, process list never leaves the machine | A source guard test forbids telemetry and unexpected network hosts; `LogScrubber` removes the user name from logs (unit-tested); the Detection log is display-only | **proven by tests**; manual log review on the QA list |
| NFR-13 Reliability | no unhandled exception crashes the tray | Global handlers in `App`; the orchestrator loop survives any failed pass (unit tests); 24 h soak | handlers and unit tests in place; soak **not run yet** |

## What is already proven automatically (CI on every PR)
- Core logic on Linux and Windows: detection, session tracking, rotation, orchestration, rendering math, catalog verification, downloads, settings.
- Windows layer on a real runner: COM wallpaper round trip, process snapshot without handle leaks, foreground hook, Run key.
- The WPF app starts its full dependency graph and every page loads with zero data-binding errors.

## How to record a measurement
Add a dated row under "Measurements" with the machine (CPU, RAM, Windows build, display setup), the build version, the command used
and the numbers. Do not overwrite earlier rows.

## Measurements
_None recorded yet._
