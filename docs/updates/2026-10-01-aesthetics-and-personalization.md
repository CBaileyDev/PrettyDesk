# Aesthetics and personalization update proposal

Date: 2026-10-01. Status: planning snapshot; remaining choices below are not
automatically approved implementation work.
The owner asked to discuss the changes first, then requested repository organization
and update documentation. This document records that scope, the observed problems
and the choices needed before feature implementation.

Status update (2026-10-02): the top-tab navigation, responsive Library cards,
twenty new default alternates, and four Rocket League landscape entries are
implemented. The existing fourteen default collections remain the category
structure; no new category was added. The navigation decision is recorded in
[ADR 0015](../adr/0015-top-tab-navigation.md), with validation and artwork counts
in the [implementation record](2026-10-02-top-navigation-and-default-art.md). The
four fan-art entries include three named-car scenes and are covered by the
[scoped prompt-mode record](2026-10-02-rocket-league-prompt-mode.md). Catalog
`2026.10.02.8` validates with 264 offline assets; the 127.52 MiB bundle has 0.48
MiB of remaining capacity. Dedicated ultrawide and portrait versions for the
twenty new default alternates remain unbuilt. Delivery, expanded game research
and optional-surface choices below remain open. Monitor-preview geometry is
covered by coordinate-based tests and light/dark captures on the current stacked
display setup; broader DPI and hot-plug validation remains pending.

## Direction and priorities

The next update should make PrettyDesk dependable to use in its packaged EXE,
quieter during gaming, and visually coherent in Windows light and dark modes.
Personalization should follow that reliable foundation.

The owner specifically requested recognizable Rocket League fan art, allowing
the game name and Octane, Fennec and Batmobile in prompts. That direction is
approved and the four-entry local pilot is implemented; wider variety and public
distribution-rights review remain separate. See the [art draft](rocket-league-fan-art.md).

| Priority | Proposed change | State |
|---|---|---|
| 0 | Clear agent guide, source/tool maps, build/test instructions and update docs | Documentation implementation |
| 1 | Make wallpaper delivery work in the EXE and explain unavailable packs | Delivery choice open; missing host confirmed |
| 2 | Reduce repeated detection work | Snapshot-sort allocation reduction and stable name-only matching reuse during idle and established sessions are implemented; a deterministic idle case reduces full matches 301→21, while CPU/frame-time measurements and native liveness remain open |
| 2 | Discover and verify the owner's installed games first | Local inventory snapshot available; broader providers pending |
| 3 | Fix alignment, monitor geometry and content availability states | Current Library capture shows consistent availability-chip baselines; monitor coordinates and display changes have test coverage; broader DPI and hot-plug validation remains |
| 3 | System/light/dark theme and calmer materials | Light/dark captures and selected-state contrast checked; top navigation supersedes the sidebar/banner concept (ADR 0015); keyboard, live high-contrast and DPI acceptance remains |
| 4 | Rocket League art pilot, then game-specific scenic variety | Four-entry offline pilot is implemented; public distribution-rights review and broader scenic variety remain |
| 5 | Research and verify a broader top-100 candidate catalog | Method proposed; full dataset not compiled |
| 6 | Optional taskbar appearance and desktop clocks/music widgets | Clock, now-playing metadata and playback visualizer prototype implemented under ADR 0008; native acceptance remains. Taskbar appearance remains open. |

## 1. Wallpaper delivery in the EXE

Observed: [catalog.src.json](../../content/catalog.src.json) has an empty
`contentBaseUrl`. Default starter collections are bundled; game thumbnails are
not the full-resolution game packs. A packaged EXE cannot download assets from an
unconfigured host. The unavailable download action also needs clear feedback.

| Option | Benefit | Cost / constraint |
|---|---|---|
| Larger offline bundle | Selected game wallpapers work immediately without a host | Bigger installer; images travel with app updates; current starter budget needs a deliberate revision |
| Small installer + signed HTTPS feed | Smaller install; independent content updates | Real hosting, signing, bandwidth and offline failure behavior must be established |
| Hybrid: small curated offline games + optional feed | Useful offline and expandable later | Additional bundle policy and coverage labels |

Recommendation for discussion: hybrid delivery, beginning with verified games on
the owner's machine. Decide storage/installer size before producing the next package.
Preserve signature/hash verification and path/download limits.

Acceptance: installer and portable EXE both apply their advertised offline wallpapers;
an unavailable pack shows the reason and a useful alternative; offline/timeout/cancel/
retry work; corrupted or unsigned content is rejected; no click silently succeeds.
Catalog presence, installed status and downloaded/ready status must be distinct.

Implementation starts in Core/Catalog, Core/Content, App composition and
Presentation Library/GameDetail view models, using the [source map](../../src/README.md).

## 2. Detection cadence and performance

Observed: `DetectionService` polls at the configured 1–10 second interval (2 seconds
by default), alongside foreground/Steam/deadline triggers. It snapshots processes and
updates the tracker on each poll. Stable name-only match results are reused during
idle and established active sessions while their inputs remain unchanged. The earlier
local process measurements were small, but they do not prove an FPS or frame-time impact.

Current behavior and remaining work:

| State | Work |
|---|---|
| Stable name-only results, idle or in an established session | Reuse results while sorted process snapshots and matcher inputs remain unchanged; still snapshot and update the tracker on every poll; rematch at the 30-second safety interval (implemented) |
| Invalidation | Changed process snapshot, foreground PID, Steam App ID or matcher; live-detail rules; exit grace; or 30-second timeout forces a fresh match (implemented) |
| Native liveness | Avoid process snapshots by checking tracked PIDs and target relevant foreground/launcher candidates; proposed and unimplemented |
| Missed start | Retain configured broad polling; a slower fallback remains a proposal pending latency and real-game measurements |

The 10-minute deterministic idle fixture reduces full `Match` evaluations from 301
to 21 while retaining all 301 process snapshots. A separate test verifies stable
matching reuse during an established active session while preserving foreground
switching and exit-grace behavior. These tests measure rule-scan work, not elapsed
CPU time or frame-time impact. Do not use monitor count as a cap on sessions: two
running games do not necessarily occupy two displays. Preserve foreground priority
and all tracked sessions without assuming the active game is the only possible game.

Liveness must cope with PID reuse, launchers handing off, denied queries, game restart,
foreground switching, sleep/resume and shutdown cancellation using permitted query
access. Do not add process-memory reads or anti-cheat-sensitive hooks. Installed-game
scans belong outside this repeated running-process loop.

Acceptance for native liveness and targeted foreground work remains: deterministic
session/race tests; baseline start/exit timing; two-game switching; and idle/active
CPU, memory and real-game frame-time percentiles on the same hardware. Run the long
soak separately and record the measured tradeoff before changing that mode.

## 3. Installed games before catalog breadth

An earlier read-only scan in this session found these 15 title/hub entries with local
files. This is a discovery snapshot, not proof they all launch or match a running rule:

| Source group | File-backed titles / hubs |
|---|---|
| Epic | Rocket League, Fortnite, Cyberpunk 2077 |
| Ubisoft / other local installs | Rainbow Six Siege, Assassin's Creed Valhalla, Black Myth: Wukong, Ghost of Tsushima Director's Cut, God of War, Ratchet & Clank: Rift Apart, Star Wars Outlaws, Call of Duty: Black Ops II |
| Xbox | Call of Duty hub, Forza Horizon 6, Minecraft for Windows |
| Java clients | Minecraft Java (multiple clients count as one title) |

Stale uninstall records and directories containing only icons are not confirmed
installations. Duplicate installations of a title should not inflate the title count.
Utilities such as Wallpaper Engine and 3DMark are excluded from the games inventory.
Arbitrary portable/unregistered games cannot be exhaustively found through launcher
manifests; retain manual add/import.

The application currently has Steam/Epic discovery. Propose additional Xbox,
Ubisoft, Battle.net/Riot and selected local-install providers with bounded read-only
scanning, deduplication and confidence labels. Do not recursively scan all drives in
the detection loop. Local path evidence stays out of committed/public reports.

Acceptance: real launches verify executable names and any path/title rules; access-
denied fallback stays safe; installed Forza Horizon 6 is not mapped to the existing
Forza Horizon 5 seed merely because the names are similar. Add missing local titles
before increasing remote catalog breadth.

## 4. Layout and theme

The owner's Oct 1 screenshots showed a misaligned availability pill, oversized
sidebar branding and a desktop preview whose vertically stacked monitors drifted
sideways. The top-tab navigation supersedes the earlier sidebar and generated-banner
concept; the decision is recorded in [ADR 0015](../adr/0015-top-tab-navigation.md).
The compact app mark and name remain available in the window, tray and About screen.
Library availability labels now share baselines, and responsive cards use row space
more consistently.

`HomeViewModel.BuildMonitors` maps reported monitor bounds at a shared pixel scale.
Tests cover side-by-side edges, negative coordinates and display-list changes. Light
and dark WPF captures show this machine's two vertically stacked 2560×1440 displays
with aligned left edges ([light](../../TestResults/top-tabs/final/Main-Home-MonitorPreview.png),
[dark](../../TestResults/top-tabs/final/Main-Home-MonitorPreview-Dark.png)). Mixed-DPI,
high-DPI and physical hot-plug validation remain open.

The app defaults to the Windows System theme and offers explicit Light and Dark
overrides. Neutral light/dark surfaces are in place, and the high-contrast navigation
focus ring uses Windows system colors distinct from its selected fill. Deriving a
corrected app accent from the selected wallpaper remains optional; it must retain a
fixed-accent fallback and never change the global Windows accent.

Remaining acceptance: inspect both themes across supported window sizes and
100/125/150/200% scaling; verify live theme changes, high-contrast keyboard focus,
Narrator, alignment and reduced-motion behavior on Windows. Preserve native dialogs
and tray lifecycle. See [AppAppearance](../../src/PrettyDesk.App/Services/AppAppearance.cs),
[Styles](../../src/PrettyDesk.App/Resources/Styles.xaml) and [ADR 0015](../adr/0015-top-tab-navigation.md).

## 5. Taskbar translucency, glass and wallpaper matching

Windows already exposes transparency effects and automatic accent selection from
the desktop background. Showing accent on Start/taskbar requires dark or custom
Windows mode. These are feasible native options, with appearance controlled by the
OS. [Microsoft's Windows color settings](https://support.microsoft.com/en-US/Windows/Experience/Personalization/personalize-your-colors-in-windows)
document their scope.

Mica and Desktop Acrylic are documented backdrop materials for application windows
on supported Windows 11 builds. That API does not establish a supported contract for
turning Explorer's taskbar into Apple's Liquid Glass.
[Microsoft DWM backdrop documentation](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type).

| Proposed option | Feasibility / next step |
|---|---|
| Unchanged (default) | Preserve the user's current taskbar |
| Native Windows appearance | Open Colors settings with clear guidance for transparency and automatic wallpaper accent; no silent OS writes |
| PrettyDesk window glass | Refine our own app surfaces using supported backdrop behavior and opaque accessibility fallback |
| Custom translucent / wallpaper-tinted taskbar | Experimental shell integration or optional companion; API, ownership and compatibility audit required |

The official [TranslucentTB project](https://github.com/TranslucentTB/TranslucentTB)
demonstrates taskbar clear/acrylic/tint options. It is a feasibility reference, not a
selected dependency or a verified integration. Do not automatically install or launch
a companion. Audit any chosen implementation against the existing no-injection/no-
foreign-window-hook constraints; reject approaches that require weakening them.

If an app-managed OS change is selected later, record a scoped ADR for the new
system-setting permission, affected properties, original-state snapshot, restore on
disable/exit/uninstall, ownership when the user changes settings, conflict handling,
Explorer restart and supported Windows builds. Keep the experimental option off by
default and immediately reversible. No taskbar setting has been changed for this plan.

Acceptance: secondary taskbars, Start/Search, auto-hide, full-screen games, multiple
displays, Explorer restart, high contrast, disabled transparency and competing
customizers all behave correctly; restore respects later user edits. Benchmark real
shell behavior. Do not advertise an exact macOS effect before an inspected prototype.

## 6. Clocks, widgets and music visualizer

Static wallpaper COM cannot draw a live clock. The first optional desktop surface is
implemented under [ADR 0008](../adr/0008-personalization-update.md): one non-topmost,
non-activating, click-through PrettyDesk window can show clock/date, Windows
now-playing metadata and a local playback visualizer. Settings provide separate
toggles, monitor selection and keyboard-accessible playback controls. The surface
pauses for active games, session lock and Battery Saver; high contrast or reduced
motion stops spectrum animation. It does not redraw the wallpaper or store or upload
audio.

This remains an ordinary-window prototype, not a guaranteed Explorer desktop layer.
Native acceptance for z-order/focus, DPI and mixed-DPI placement, Explorer restart,
audio-device changes, and CPU/GPU/frame-time impact remains open. Do not inject into
games or repeatedly render/reapply the full wallpaper each second. Any broader widget
surface or customization needs a scoped ADR before implementation.

The visualizer uses transient samples from the default multimedia output through
documented WASAPI loopback; it does not capture the microphone or store or upload
audio. Default-device changes are checked periodically and failures are surfaced.
A broader output-device selector remains deferred. See
[Microsoft's loopback recording documentation](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
and the accepted implementation scope in [ADR 0008](../adr/0008-personalization-update.md).

The animated spectrum is capped at about 15 FPS and stops when any game is detected,
the session is locked, Battery Saver is on, or Windows accessibility preferences
disable client-area animation. The clock, now-playing and visualizer toggles
independently control their content; when all are off, the window hides and its timer
and capture stop. There is no separate master switch in this prototype. Exit closes
the window and releases capture.
Native acceptance still needs focus/z-order, DPI, Explorer restart, device-change and
real CPU/GPU/frame-time
checks. Broader widget surfaces or customization need a separate scoped ADR.

## 7. Game catalog and image variety

First verify the owner's local games; then compile a dated 100-title candidate list.
Do not claim there is one universal PC ranking. The official
[Steam most-played chart](https://store.steampowered.com/charts/mostplayed/) ranks its
own concurrent players; supplement non-Steam titles with a separate documented
metric/source such as [Newzoo's PC/console rankings](https://newzoo.com/articles/august-2026-pc-console-rankings).
Record date, geography/platform coverage and metric without combining incomparable
counts. Inclusion is a product decision; popularity is not executable-rule validation.

Give each game its own recognizable scene vocabulary and deliberate shot plan:
subject, point of view, environment, scale, time/weather, palette and desktop safe
zones. A different color on the same vista is not sufficient variety. The four-entry
[Rocket League pilot](rocket-league-fan-art.md) is implemented and its generated crops
were reviewed; use that as a process reference when broader variety is selected. Keep
originals/provenance and avoid batch-approving unseen images.

## Decisions for discussion and implementation gates

Open choices: offline/online/hybrid delivery and package size; remaining theme details;
taskbar native guidance versus experimental companion; top-100 ranking methodology.
The first desktop-surface scope is recorded in ADR 0008, and the named Rocket League
art direction is already approved.

Implement in the priority order above. Each feature needs relevant deterministic
tests, actual Windows verification where applicable, inspected UI/art output, a fresh
package when runtime content changes, and clear unrun release checks. Preserve the
[earlier review evidence](../REVIEW_2026-10-01.md); it cannot validate future changes.
