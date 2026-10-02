# Aesthetics and personalization update proposal

Date: 2026-10-01. Status: planning; application features below are not implemented.
The owner asked to discuss the changes first, then requested repository organization
and update documentation. This document records that scope, the observed problems
and the choices needed before feature implementation.

## Direction and priorities

The next update should make PrettyDesk dependable to use in its packaged EXE,
quieter during gaming, and visually coherent in Windows light and dark modes.
Personalization should follow that reliable foundation.

The owner specifically requested recognizable Rocket League fan art, allowing
the game name and Octane, Fennec and Batmobile in prompts. That direction is approved;
generation and pipeline integration are still pending. See the [art draft](rocket-league-fan-art.md).

| Priority | Proposed change | State |
|---|---|---|
| 0 | Clear agent guide, source/tool maps, build/test instructions and update docs | Documentation implementation |
| 1 | Make wallpaper delivery work in the EXE and explain unavailable packs | Delivery choice open; missing host confirmed |
| 2 | Reduce broad detection work while a known session is active | Design proposed; benchmark and logic work pending |
| 2 | Discover and verify the owner's installed games first | Local inventory snapshot available; broader providers pending |
| 3 | Fix alignment, monitor geometry and content availability states | Problems identified; UI changes pending |
| 3 | System/light/dark theme, calmer materials and generated sidebar banner | Visual direction proposed; mockups/banner pending |
| 4 | Rocket League art pilot, then game-specific scenic variety | Named fan-art direction approved; pilot pending |
| 5 | Research and verify a broader top-100 candidate catalog | Method proposed; full dataset not compiled |
| 6 | Optional taskbar appearance and desktop clocks/music widgets | Feasibility and lifecycle prototypes pending |

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

Observed: `DetectionService` continues evaluation on its two-second polling cadence
while a game runs, alongside foreground/Steam/deadline triggers. The earlier local
process measurements were small, but they do not prove an FPS or frame-time impact.

Proposed operating modes:

| Mode | Work |
|---|---|
| No active game | Existing normal discovery cadence, configurable within sensible bounds |
| Known active session | Lightweight liveness checks of tracked game processes; skip repeated full catalog matching when nothing relevant changes |
| Foreground or launcher signal | Evaluate the relevant candidate to support deliberate game switching |
| Exit / missed signal | Preserve exit grace and return to broad discovery; a slower fallback scan catches missed starts |

A fallback interval around 30 seconds is a starting experiment, not a shipped value.
Do not use monitor count as a cap on sessions: two running games do not necessarily
occupy two displays. Preserve foreground priority and all tracked sessions without
assuming the active game is the only possible game.

Liveness must cope with PID reuse, launchers handing off, denied queries, game restart,
foreground switching, sleep/resume and shutdown cancellation using permitted query
access. Do not add process-memory reads or anti-cheat-sensitive hooks. Installed-game
scans belong outside this repeated running-process loop.

Acceptance: deterministic session/race tests pass; broad evaluation counts fall during
a long unchanged session; start/exit timing remains within the baseline requirement;
two-game switching still works; compare idle/active CPU and memory plus actual game's
frame-time percentiles on the same hardware. Record the measured tradeoff before
choosing default intervals. Run the long soak separately.

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

## 4. Layout, theme and banner

The owner's screenshots show a misaligned availability pill, oversized sidebar
branding and a desktop preview whose vertically stacked monitors drift sideways.
Windows reports both screens at X=0; `HomeViewModel.BuildMonitors` adds a per-monitor
horizontal gap, creating the offset. Fix preview geometry without changing Windows
display arrangement.

Proposed visual changes:

- Center availability labels within their controls and use consistent padding/baselines.
- Map preview position from monitor bounds; put readable display name, resolution
  and primary indicator outside tiny image text. Support negative coordinates,
  unequal resolutions, mixed DPI and more than two monitors.
- Replace the sidebar's large PrettyDesk wordmark block with a restrained generated
  banner. Keep app identity accessible in the window, tray and About screen.
- Use neutral charcoal surfaces in dark mode and soft neutral light surfaces in light
  mode; reserve color for actions/selection. Keep glass depth subtle and readable.
- Default to System; offer explicit Light/Dark overrides. The application already
  follows Windows, so this is a palette/control refinement, not a missing OS watcher.
- Optionally derive an app accent from the selected wallpaper, with contrast correction,
  a fixed-accent fallback and stable behavior during rotations. App accent must not
  silently change the global Windows accent.
- Respect high contrast, Windows transparency preferences and reduced motion.
  Avoid continuous decorative animation while the window is hidden.

Banner draft: a panoramic abstract glass sculpture with soft silver and muted cool
reflections against a charcoal-to-neutral background, generous breathing room,
clean silhouette, no embedded text or logos. Prepare matched light/dark crops and
preview at the actual sidebar size before integration. Generation is pending.

Acceptance: inspect actual WPF renders in both themes at supported window sizes and
100/125/150/200% scaling; verify alignment, contrast, focus and keyboard workflows;
theme changes apply live without flicker. Preserve native dialogs and tray lifecycle.
Use [AppAppearance](../../src/PrettyDesk.App/Services/AppAppearance.cs),
[Styles](../../src/PrettyDesk.App/Resources/Styles.xaml) and the page/view-model pair.

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

Static wallpaper COM cannot draw a live clock. Prototype a separate optional desktop
surface, beginning with clock/date and then now-playing controls. Define z-order,
click-through/edit mode, per-monitor placement, DPI, Explorer lifecycle and persistence.
Do not inject into games or repeatedly render/reapply the full wallpaper each second.

A visualizer can analyze transient local playback samples using documented WASAPI
loopback. Decide output-device selection, silence/device changes and capture scope;
no microphone or recording file is needed for the proposed playback-only effect.
[Microsoft loopback recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording).

Start with a capped 15–30 FPS experiment. Pause surfaces hidden by full-screen games
on the relevant monitor, when locked and where power policy requires it. Reduced
motion/static fallback and a kill switch are required. Acceptance includes real CPU/GPU/
frame-time measurements, no focus theft, no audio storage/network upload and restoration
of normal desktop behavior when disabled. Widget implementation requires its own ADR.

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
zones. A different color on the same vista is not sufficient variety. Start with the
[Rocket League pilot](rocket-league-fan-art.md), inspect actual generated crops, then
expand. Keep originals/provenance and avoid batch-approving unseen images.

## Decisions for discussion and implementation gates

Open choices: offline/online/hybrid delivery and package size; final theme mockups
and banner; taskbar native guidance versus experimental companion; first widget scope;
top-100 ranking methodology. The named Rocket League art direction is already approved.

Implement in the priority order above. Each feature needs relevant deterministic
tests, actual Windows verification where applicable, inspected UI/art output, a fresh
package when runtime content changes, and clear unrun release checks. Preserve the
[earlier review evidence](../REVIEW_2026-10-01.md); it cannot validate future changes.
