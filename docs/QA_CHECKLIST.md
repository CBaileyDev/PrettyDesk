# Manual QA checklist (SPEC 11.2)

Run this before every release (stable and beta). Automated tests prove the logic and that the app starts and every page binds; they
cannot prove COM behavior on every display, kernel anti-cheat friendliness, or the installer. Copy this file into the release PR,
tick each line, and write the machine and build next to anything you could not run (a skipped item is a known risk, not a pass).

**Build under test:** `__________` (from About) **Tester:** `__________` **Date:** `__________`

## Local execution record — 2026-10-01

Tester: Codex on Windows 11 Pro build 26200, Ryzen 9950X3D, x64, two 2560×1440 displays. Builds: acceptance 1.0.0 / 1.0.1 for installed update/uninstall; self-contained ReadyToRun acceptance 1.0.2 for performance. Acceptance commands and isolated data paths are compiled out of normal release builds.

| Check | Verdict / scope |
|---|---|
| Offline content | PASS: all 13 default collections resolve a real 3840×2160 asset and thumbnail without starting network refresh; bundle hashes and schema validated |
| Catalog signing / downloads | PASS: shipped key verifies a real HTTPS fixture; generated wallpaper and thumbnail downloaded with hash checks; corrupt assets, tampered and unsigned feeds rejected, cached catalog retained |
| Velopack install/update | PASS: silent isolated install of 1.0.0; backend check + download + apply/restart into 1.0.1 from a real local feed; full and delta packages produced |
| Uninstall | PASS: actual updater uninstall hook ran; both original image hashes, position and color restored; Run value removed. The native Yes/No prompt also passed twice with isolated data: No kept settings/all five directories, Yes deleted them |
| Slideshow restore | PASS: real COM test restores source items, shuffle option and interval |
| UI | PASS: WPF pages instantiate with no binding errors; changed Home banner wraps correctly in captured render. Native Mica, complete keyboard/Narrator and DPI matrix NOT RUN |
| CPU / startup / latency | PASS within the local scopes recorded in PERF.md; active-idle memory exceeded 100 MB in 1.0.2 (**FAIL**), 1.0.3 retest and 24 h soak RUNNING |
| Public signing, production host, ARM64 run, other OS versions, five real games / anti-cheat | NOT RUN / unavailable or pending owner configuration |

Evidence lives in `docs/evidence/2026-10-01`; raw UI renders are in `TestResults/ui`. The full pre-release checklist below remains unticked where its broader criterion has not actually been exercised. M7 is not complete.

## 0. Before you start
- [ ] Fresh Windows 11 user profile or VM snapshot (so first-run, onboarding and uninstall are real).
- [ ] Note your current wallpaper(s) and, if any, Spotlight / slideshow / Wallpaper Engine / Lively status.
- [ ] Open Settings > Advanced > Detection log (used to verify game exe names below).

## 1. Install, first run, update, uninstall
- [ ] `PrettyDeskApp-win-x64-stable-Setup.exe` (or arm64) installs **without a UAC prompt**, creates a Start menu shortcut, and starts the app.
- [ ] SmartScreen shows the signed publisher (stable builds). Unsigned pre-releases are expected to warn.
- [ ] First run opens onboarding; the setup quiz preselects the collections in ART_DIRECTION 3; "Start with Windows" is **asked**, not silently enabled (`HKCU\...\Run` has no PrettyDesk value until you answer).
- [ ] Choosing "Surprise me" selects one starter wallpaper per collection.
- [ ] Closing the window hides to the tray; the one-time tray hint appears once.
- [ ] Install the **previous** release, then update to the new one through the About page ("Restart to update") **and** by simply restarting. Settings, favorites, downloaded packs and the original-wallpaper backup survive.
- [ ] Beta toggle: Settings > General > Get beta versions on/off changes which release the next check offers.
- [ ] Portable zip: About says updates are not supported; nothing is written outside `%LOCALAPPDATA%\PrettyDesk` and its own folder.
- [ ] **Uninstall** (Settings > Apps): the wallpaper you had before PrettyDesk is back on every monitor; the `Run` value is gone; the Yes/No question about your data appears with **No** as the default; answer No, reinstall, and your settings are still there; answer Yes next time and `%LOCALAPPDATA%\PrettyDesk` loses settings, packs, user images and backup.
- [ ] After uninstall, record exactly what remains under `%LOCALAPPDATA%\PrettyDeskApp` and `%LOCALAPPDATA%\PrettyDesk` (ADR 0006 assumes Velopack leaves our data folder alone).

## 2. Detection and switching (use Settings > Advanced > Detection log)
For each game below, launch it, confirm the Detection log shows the exe name from the catalog, and confirm: wallpaper switches after the detect delay, returns to Default after the exit grace, no flicker, no switch while only the launcher is open.
- [ ] Steam game: __________   [ ] Epic game: __________   [ ] Riot game: __________   [ ] Battle.net game: __________   [ ] Xbox / Store game: __________
- [ ] A game with **kernel anti-cheat** (Vanguard, EAC or BattlEye): no anti-cheat warning, no kick, no FPS change (PresentMon or in-game FPS on/off, 3 runs). Handle access is limited to `PROCESS_QUERY_LIMITED_INFORMATION` (a source guard test also enforces this).
- [ ] Fix any wrong exe name in `content/catalog.src.json`, set `verified` to today's date, and re-run `python tools/validate_catalog.py`.
- [ ] Two games running: the foreground one wins; alt-tab switches back and forth without wallpaper churn.
- [ ] Game crashes within 20 s of launch, and game restarts itself within the grace period: no flip-flop.
- [ ] Unknown game in the foreground for several minutes: one quiet hint, "Add game" opens the add flow; no hint for browsers, editors or launchers.
- [ ] Add a custom game by exe name; it matches, can use its own images, and can be removed.
- [ ] Pack not downloaded yet: the current wallpaper stays until the pack arrives, then switches (Library shows progress; failure shows a human message).

## 3. Displays and rendering
- [ ] 1080p single monitor. [ ] Laptop at 150%. [ ] 4K at 200%. [ ] Dual monitors with mixed DPI. [ ] Ultrawide 3440x1440. [ ] Portrait-rotated monitor.
- [ ] The applied image is pixel-exact for each monitor (no blur or shift; check the PNG in `%LOCALAPPDATA%\PrettyDesk\cache\render`).
- [ ] Hot-plug a monitor mid-game: it gets the right wallpaper within a few seconds. Dock and undock.
- [ ] Monitors setting "game only on secondary": primary stays on Default.
- [ ] Rotation: interval, "on unlock" and "every launch" behave; no immediate repeats; order Shuffle vs Sequential.

## 4. System states
- [ ] Sleep and resume (including a multi-day sleep via clock change): one rotation, not a burst.
- [ ] Lock and unlock. [ ] Fast user switching (settings are per user). [ ] RDP session (wallpaper left alone or restored sensibly).
- [ ] **Explorer restart:** `taskkill /f /im explorer.exe`, then start Explorer: the wallpaper is re-applied.
- [ ] Battery Saver on: rotation pauses if that option is on. [ ] Light/dark switch with "follow Windows theme" on.
- [ ] Unactivated Windows (watermark/personalization restrictions): app shows a human message instead of failing silently.
- [ ] **Spotlight** active before first apply: background is left untouched, applying is blocked, and the Home/onboarding explanation tells the user to select Picture or Slideshow. Test migration of an older Spotlight backup separately.
- [ ] **Wallpaper Engine** and **Lively** running: the conflict banner appears on Home with a clear next step.
- [x] Slideshow source items, shuffle and interval round-trip through backup/apply/restore on the local Windows desktop (2026-10-01 integration test). Full installer/UI flow with an actively rotating slideshow still needs the release matrix.

## 5. Restore and "leave"
- [ ] Settings > Restore my original wallpaper works even after the original file was deleted (uses the backup copy).
- [ ] "Restore on exit" puts the original back at Quit and never overwrites it again before the next start.
- [ ] Reset PrettyDesk: settings and downloaded content cleared; the original-wallpaper backup is kept.

## 6. UI quality
- [ ] Keyboard only: tab through every page, every control has a visible focus ring and a spoken name (Narrator).
- [ ] High contrast theme and "Animation effects" off look correct.
- [ ] Every action that takes time shows a loading state; every error has a plain-language message; every list has an empty state (try offline, no games installed, empty library search).
- [ ] No layout shift when thumbnails load; window remembers size and position; minimum size respected.
- [ ] About shows version, licenses (third-party notices), the disclaimer, and working links. Privacy statement matches reality (no telemetry).
- [ ] Dashboard look (ADR 0016), light, dark and high contrast: sidebar and title bar read as one frame; cards, segmented controls, setting rows and primary buttons have enough contrast; the selected nav item, selected segment and focus rings are visible; nothing is blurred or transparent. Check Home, Library (cards and filters), a game page, Defaults, Settings, About, onboarding and a message dialog at 860x560 and maximised.
- [ ] Home hides "Pause" buttons while paused or blocked and shows "Resume" only while paused; Tab order stays sensible when buttons appear and disappear.

## 7. Content and art
- [ ] Every shipped wallpaper passed the ART_DIRECTION 6 review checklist (no text, no seams, calm left 15 % and bottom 8 %, not an official asset).
- [ ] The catalog fetch works from the content host; an invalid or older catalog is ignored silently; a tampered signature is rejected (try with a modified `catalog.json.sig`).
- [ ] Storage: Settings shows usage; "Clear downloaded content" works; cap eviction keeps installed games' packs.

## 8. Soak and performance (record results in `docs/PERF.md`)
- [ ] 10 minute idle: `tools/perf/soak.ps1` (NFR-1, NFR-2).
- [ ] 24 hour soak with a few game launches: `tools/perf/soak.ps1 -Hours 24` (NFR-2, NFR-13).
- [ ] Cold start to tray < 1.5 s (NFR-5); game start/exit latency (NFR-3, NFR-4).

## 9. Platforms
- [ ] Windows 11 23H2. [ ] 24H2. [ ] 25H2 or latest. [ ] ARM64 device. [ ] Windows 10 22H2 smoke test (best effort).

## 10. Sign-off
- [ ] Zero open blocker findings. Known issues copied to the release notes.

## Local review evidence — 2026-10-01

See [the full review](REVIEW_2026-10-01.md) for scope and remaining release gates. These checks do not close the broader
hardware/manual rows above:

- [x] Read-only real launcher scan identifies installed Cyberpunk, Fortnite, and Rocket League; the current running Rocket League process matches its catalog rule. No real game launched by the probe.
- [x] Corrupt/null JSON recovery, catalog/signature/asset resource bounds, stalled-body cancellation, and concurrent download regressions.
- [x] A failed durable wallpaper backup blocks applying; a later successful save retries the same original snapshot (real Windows regression).
- [x] Active ten-minute tray sample: 0.023% average CPU, 63.0 MiB peak private memory on the local 32-thread desktop.
- [x] Real Quit restores the two-monitor slideshow configuration, including source items and timing.
- [x] WPF page smoke and captured light/dark/minimum-window layouts inspected; seven App tests also passed under the current 32 MiB managed heap cap.
- [x] Current cached synthetic game start/exit benchmark, five runs: 3.757–3.801 s entry and 10.058–10.308 s exit; real Quit restores the original configuration afterward.
- [x] Final 15-check gate, including locked x64/ARM64 publishing and dependency audit; 804 distinct .NET/Python tests passed including the separate HTTPS fixture.
- [ ] Complete a new 24-hour soak of the current build. The older run was interrupted and has no completion summary.
- [ ] Verify all 46 catalog games individually; zero rules currently have dated verification. Shared executable names under denied path access need particular attention.
