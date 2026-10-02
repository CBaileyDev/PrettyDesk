# Performance and reliability record (SPEC NFRs)

This file records how each non-functional requirement is **measured** and what has actually been **measured so far**.
A row stays "not measured yet" until a person has run it on real Windows hardware; nothing here is estimated.

| NFR | Target | How to measure | Status |
|---|---|---|---|
| NFR-1 Idle CPU (window closed, 10 min, 4-core laptop) | < 0.2 % average, no sustained spikes | `tools/perf/soak.ps1` (default 10 min) | **PASS locally: current 1.0.6 mean 0.02327%**, peak sample 0.622%; 32 logical cores, not the required 4-core laptop |
| NFR-2 Idle memory (window closed) and 24 h growth | < 80 MB private bytes, no growth over 24 h | `tools/perf/soak.ps1 -Hours 24`; compare first and last 10 % of samples | Current active 1.0.6 ten-minute peak **63.0 MiB, PASS locally**. Older 1.0.2 exceeded 100 MB; its 24 h run was interrupted. No completed 24 h verdict |
| NFR-3 Game start to wallpaper (pack cached) | <= `detectDelay` + 1.5 s | `--acceptance-switch-benchmark`: foreground fixture, cached art, actual COM path observed, 5x | **PASS for current synthetic fixture: 3.757–3.801 s** (default 3 s delay, 2 s poll); actual game launch timing NOT RUN |
| NFR-4 Game exit to default | <= `exitGrace` + 1.5 s | Same, killing the fixture process | **PASS for current synthetic fixture: 10.058–10.308 s** (default 10 s grace); actual game exit timing NOT RUN |
| NFR-5 Cold start to tray icon | < 1.5 s on an SSD | `tools/perf/startup.ps1`: five fresh processes, receipt immediately after `TaskbarIcon.ForceCreate` | **PASS locally: external median 446 ms, worst 870 ms**; shell registration measured, visual appearance not screen-timed |
| NFR-6 Permissions | no admin, per-user, no services/drivers/tasks | Enforced by `app.manifest` (`asInvoker`), source guard tests, and the QA install check | **enforced in code and tests**; install check is on the QA list |
| NFR-7 Offline | fully functional with the bundled starter set | Block the network, start the app, switch games and rotate | covered by unit tests (`CatalogService`, `ContentLibrary`); end-to-end on the QA list |
| NFR-8 Platforms | win-x64 and win-arm64 native | Locked self-contained ReadyToRun publish for both RIDs; hardware execution separately | **Both current publishes passed locally**, x64 runtime exercised; ARM64 execution not yet done |
| NFR-9 Per-Monitor-V2 DPI | correct at 100-300 % | `app.manifest` declares it; verify on the QA display matrix | declared; visual check on the QA list |
| NFR-10 Accessibility | keyboard, focus, AutomationProperties, AA contrast | A static test checks every bound control; a Windows UI test instantiates every page with zero WPF binding errors (CI) | **partially proven**; screen reader pass is on the QA list |
| NFR-11 Localization-ready | all strings in `.resx` | `python3 tools/strings.py check` in CI | **proven by CI** |
| NFR-12 Privacy | no telemetry, process list never leaves the machine | A source guard test forbids telemetry and unexpected network hosts; `LogScrubber` removes the user name from logs (unit-tested); the Detection log is display-only | **proven by tests**; manual log review on the QA list |
| NFR-13 Reliability | no unhandled exception crashes the tray | Global handlers in `App`; the orchestrator loop survives any failed pass (unit tests); 24 h soak | handlers and unit tests in place; original soak **INTERRUPTED**, a completed 24 h run is still required |

## What is already proven automatically (CI on every PR)
- Core logic on Linux and Windows: detection, session tracking, rotation, orchestration, rendering math, catalog verification, downloads, settings.
- Windows layer on a real runner: COM wallpaper round trip, process snapshot without handle leaks, foreground hook, Run key.
- The WPF app starts its full dependency graph and every page loads with zero data-binding errors.

## How to record a measurement
Add a dated row under "Measurements" with the machine (CPU, RAM, Windows build, display setup), the build version, the command used
and the numbers. Do not overwrite earlier rows.

## Measurements
### 2026-10-01 — local Windows x64

Windows 11 Pro build 26200, Ryzen 9 9950X3D (16 cores / 32 threads), 61.4 GiB installed RAM, RTX 4080, two 2560×1440 monitors. Other development/image-generation work was active. These results do not substitute for a 4-core laptop, ARM64, anti-cheat game tests, or the complete display/state matrix.

| Build / check | Measured result | Evidence |
|---|---|---|
| Installed acceptance 1.0.1, window closed, paused; detection still running; 10 min | 120 samples over 601.6 s; CPU mean 0.0131167%, peak 0.078%; private bytes peak 71.7 MB | `evidence/2026-10-01/idle10m.csv` and `.summary.json` |
| Self-contained ReadyToRun acceptance 1.0.2, five fresh process starts | External 870, 439, 491, 416, 446 ms; median 446 ms. Internal tray registration 668, 380, 403, 363, 382 ms | `evidence/2026-10-01/startup.json` |
| Same 1.0.2, default timing, foreground custom-game fixture, cached art | Start 3806, 3750, 3718, 3718, 3713 ms; exit 10292, 10294, 10315, 10314, 10307 ms. Actual desktop COM paths sampled every 25 ms | `evidence/2026-10-01/switching.json` |
| Same 1.0.2, active Default with fixed mb-01; window closed | 24 h soak started Oct 1 at 01:36:47 Eastern. Review found both original process and sampler absent, with CSV ending around 15:01 and no completed summary. **INTERRUPTED, not a pass.** | `evidence/2026-10-01/soak24h-run.json`, `soak24h.csv` |

The 10-minute run showed startup/warm-up growth; it is not proof of no growth over 24 hours. Inspect the 24-hour first/last 10% and handle/thread trends before signing off NFR-2/NFR-13. M7 remains incomplete until that run and the remaining hardware/game/manual checks finish.

### Active-idle memory failure and retest

The 1.0.2 active soak exceeded 100 MB private bytes after roughly 20 minutes, so it fails the 80 MB target even if it completes
without crashing. Runtime counters showed about 52.2 MB GC-committed memory, with about 6.1 MB surviving the last collection.
Build 1.0.3 avoids temporary enumerable allocations in the game/process scan and uses `System.GC.ConserveMemory=7`.
This setting trades more frequent collections for a smaller heap; see [Microsoft's GC configuration reference](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#conserve-memory).
Its separate active-idle sample (`evidence/2026-10-01/idle-active-memory7.csv`) completed at 77.5 MB peak over 10 minutes,
but the process reached 83.4 MB shortly afterward, so the setting alone did not meet the target.
Build 1.0.4 also bounds the managed heap to 32 MiB; that does not cap native rendering memory. Its separate sample is
`evidence/2026-10-01/idle-active-memory32.csv`. UI, switching, and longer-run regression checks are required before accepting
that setting. The original 24-hour run remains useful as a baseline reliability record.

### 2026-10-01 — review of the hardened build

Self-contained ReadyToRun acceptance 1.0.6 used isolated settings and an active fixed Default wallpaper with native
two-second detection enabled. The running Rocket League was excluded from switching during idle measurement.
The real Quit handler restored the original two-monitor slideshow configuration after the run.

| Check | Measured result | Evidence |
|---|---|---|
| Active tray idle, ten minutes | 120 samples over 601.8 s; CPU mean 0.02327%, peak sample 0.622%; private bytes peak 63.0 MiB; first/last tenth 58.9 → 61.9 MiB; average handle count change -7 | `evidence/2026-10-01/review-final-idle.csv` and `.summary.json` |
| Real Quit restoration | Original background type, COM position, color, slideshow source items, options, and interval all matched the saved original | `evidence/2026-10-01/review-idle-restore.json` |
| Five cached synthetic game sessions, current 1.0.6 | Entry 3.757–3.801 s; exit 10.058–10.308 s; actual desktop COM paths observed. Meets the default 4.5 s / 11.5 s limits for the fixture | `evidence/2026-10-01/review-switching.json` |
| WPF smoke under 32 MiB managed-heap limit | Seven App tests passed, including page binding checks and captures; all page tests ran under `DOTNET_GCHeapHardLimit=2000000` (hexadecimal bytes) | `TestResults/review-final-ui/` |

This is the same 32-logical-core desktop, not the required four-core laptop. The single CPU peak is not sustained CPU load.
Native image memory is outside the managed heap limit. Ten minutes and page smoke tests do not establish 24-hour stability,
maximum-image stress, or sleep/display/hardware reliability. The first review sample (`review-active-idle.csv`) was interrupted
by its scheduled Quit before the sampler endpoint and must not be counted as a completed ten-minute gate.
