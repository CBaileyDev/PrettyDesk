# 2026-10-02: Navigation and default artwork

Status: implemented and locally validated; manual Windows accessibility and native-surface checks remain pending.

## Plan and reasoning

The main window currently spends 214 DIPs on a left navigation rail. Moving its five
existing destinations into a top tab row should give the Library and wallpaper grids
more width, especially at the existing 860-DIP minimum window size. Use WPF tab
semantics, retain the existing cached page host and short transition, and give focus
and selection explicit theme-aware states. High contrast uses Windows system brushes.

The catalog already has 14 default collections, but most expose only one wallpaper.
Adding labels would increase taxonomy without filling those shelves. Instead,
prioritize four already-authored landscape prompts from distinct collections:
Oak and Linen Morning, Still Lake Morning, Harbor in Morning Haze and Graphite Aura.
Keep each collection's existing universal `starter: true` choice. Add these reviewed
alternates with the `bundled` tag, which makes their currently built 16:9 images
available offline without changing the one-starter-per-collection rule. The renderer
uses its documented closest-variant crop for other monitor shapes; dedicated
ultrawide and portrait masters remain a later art pass.

A follow-up pass fills four more sparse collections rather than introducing new
categories: Folded Paper (Clean White), Misty Fern Forest (Sage Botanical), Night
Coastline (Steel Blue Night) and Arches in Afternoon Sun (Architectural). The
selection adds a clean abstract, nature, dark coastal and geometric architectural
option while keeping the existing starter choices and category structure intact.
Review both the full-resolution output and its thumbnail, with particular attention
to the icon column and taskbar-safe edge; regenerate when the scene violates its
prompt or becomes too busy in those areas.

A third pass targets four of the five remaining one-item default shelves: Reading Nook at
Night (Cozy Lo-fi), Eclipse Corona (Deep Space), Monolith in the Fog (Matte Black)
and Clay Shapes (Pastel Dream). These add a warm interior, celestial focal image,
dark atmospheric form and light pastel render without increasing category count.
The current offline bundle has 14.3 MiB of headroom, so keep the additions only if
the assembled size remains under the 128 MiB policy.

Neon Minimal is the final single-item pack. Add its authored Minimal Synth Sunset
prompt only if the x4 render keeps the lower interface area quiet and the total
bundle remains within budget; this brings every existing default collection to at
least two wallpapers without creating another category.

At the 860-DIP window floor, the collection gallery previously wrapped to two
cards per row because its 16-DIP gap made the third tile miss the viewport by a
few pixels. Reducing only the horizontal gap to 8 DIPs fits three 236-DIP cards
across at minimum width and retains four columns at the normal window size.

## Work checklist

- [x] Replace the left rail with a labeled top tab strip; preserve page and tray behavior.
- [x] Fit one more collection card across at the 860-DIP minimum width.
- [x] Record per-candidate source coverage before generating anything.
- [x] Generate, render, inspect and approve selected landscape masters.
- [x] Assemble only reviewed assets and verify hashes, catalog schema and bundle size.
- [x] Build the app with zero warnings; run documentation and formatting checks.
- [x] Measure and reduce recurring process-snapshot allocation without increasing measured cost at 512 rows.
- [x] Record any native UI, keyboard, contrast, display or performance checks not run.

## Results

The top tab bar keeps the five localized destinations, native title-bar controls,
tray/page lifecycle and cached page host. The WPF smoke suite passes at both the
normal and 860×560 minimum sizes; dark-mode capture, programmatic selection and
focus moving from Settings to About with selection following it were checked.
At minimum width the collection gallery now presents three cards across. Each tab
has a 44-DIP minimum hit area, a visible focus outline, and a non-color selected
marker. Selected colors meet normal-text contrast locally, and high contrast
resolves through Windows system brushes. Calculated selected-label contrast is
7.46:1 in light mode and 10.15:1 in dark mode.

The [light](../../TestResults/top-tabs/final/Main-Home-MonitorPreview.png) and
[dark](../../TestResults/top-tabs/final/Main-Home-MonitorPreview-Dark.png) Home
captures also include the lower display-preview card. Both show this machine's two
2560×1440 displays stacked with aligned left edges; the earlier sideways drift does
not reproduce. View-model tests cover edge-to-edge side-by-side monitors, negative
coordinates and display-list updates. The current [Library capture](../../TestResults/top-tabs/final/Library.png)
likewise shows availability chips on consistent baselines. These renders cover the
current setup only, not mixed-DPI or high-DPI arrangements.

The initial four 1672×941 masters were generated and reviewed in the translucent
safe-zone contact sheet at `TestResults/art-2026-10-02/review.png`. The overlays
mark the project's left 12% desktop-icon column, bottom 48 px/1080p taskbar and
each focal point. Every image has a hash-verified 3840×2160 output and 640×360
thumbnail. They are bundled offline as alternates while each collection retains
its single `starter: true` wallpaper. At that stage catalog version `2026.10.02.2`
validated with 232 referenced assets and the offline bundle was 107.22 MiB.

The follow-up pass adds four more original 1672×941 masters. Folded Paper keeps
its folds centered; Misty Fern Forest puts crisp foliage in the right-center and
reserves a blurred lower strip; Night Coastline uses three isolated beacon lights
over smooth dark water; Arches in Afternoon Sun uses a clean limestone colonnade
against empty sky. The first fern attempt was rejected for a busy taskbar edge, the
first coast attempt for lights that read as a settlement, and the first arch attempt
for unwanted distant trees. The selected alternatives were rebuilt through local
RealESRGAN x4 and reviewed at 3840×2160 and 640×360 before approval. Each remains
a bundled alternate, preserving one starter per collection. Catalog version
`2026.10.02.3` validates with 240 referenced assets; the offline bundle is 113.70
MiB against the 128 MiB policy.

The refreshed `GENERATION_STATUS.json` records 202/286 landscape masters, 32/286
ultrawide masters, 32/286 portrait masters, 196 built wallpapers and 177 catalog
wallpaper entries across all 14 bundled starter collections. Artwork generation
remains in progress.

The third batch adds four more 1672×941 masters to those sparse shelves. Reading
Nook at Night reserves a simple wood strip below the cozy scene for the taskbar;
Eclipse Corona keeps its feathered light centered against smooth black; Monolith
in the Fog uses a single clean-edged form; Clay Shapes preserves broad cream space
around the pastel forms. All four used their prompt-configured RealESRGAN x4 model,
were reviewed as 3840×2160 renders and 640×360 thumbnails, and were assembled only
after that review. These twelve new default alternates span twelve existing
collections; each keeps its original starter. Catalog version
`2026.10.02.4` validates with 248 referenced assets; the offline bundle is 117.94
MiB, leaving 10.06 MiB below its 128 MiB limit.

After the third batch, coverage was 206/286 landscape masters, 32/286 ultrawide masters, 32/286
portrait masters, 200 built wallpapers and 181 catalog wallpaper entries. All 14
bundled starter collections remain intact; artwork generation remains in progress.

Minimal Synth Sunset completes the one remaining single-item default pack. Its
centered orange half-sun and faint horizon grid were reviewed full size and as a
thumbnail, then built with the pack's x4 model and tagged as a bundled alternate.
The set now has thirteen new alternates across thirteen default collections; the
existing Liquid Glass collection already had three wallpapers. Every default
collection now offers at least two choices, while each retains exactly one starter.
Catalog version `2026.10.02.5` validates with 250 referenced assets; the offline
bundle is 118.71 MiB, leaving 9.29 MiB under policy.

The refreshed coverage report records 207/286 landscape masters, 32/286 ultrawide
masters, 32/286 portrait masters, 201 built wallpapers and 182 catalog wallpapers.
All 14 starter collections remain bundled and artwork generation remains in progress.

The recurring detection snapshot sort now copies PID keys and process references
into parallel arrays before sorting. Its five-trial local microbenchmark reduces
allocation by 51–65% at 16–512 rows with comparable or lower median elapsed time;
the shuffled-order regression test passes. See [PERF.md](../PERF.md) for the
measurement and its limits.

A follow-up review found that unchanged name-only results were still rematched on
every poll while idle and during established sessions. The service now reuses those
results until an input changes or its 30-second safety refresh is due, while
continuing process snapshots and tracker observations each poll. A fake-clock
ten-minute idle test kept all 301 snapshots and reduced full name-only matcher
evaluations from 301 to 21 (93.0% fewer); lightweight live-detail checks still run
each poll. A separate established-session test preserves debounce, foreground
switching and exit-grace behavior. Foreground/Steam/config/process invalidation and
live-detail rematching remain covered, including process identity changes that only
update process start time. The fallback uses monotonic `TimeProvider` timestamps,
including across a simulated forward wall-clock jump. This counts rule scans, not
elapsed CPU time; the hardware idle soak was not repeated. See [PERF.md](../PERF.md).

The high-contrast navigation focus ring now uses Windows `HighlightText` against
the selected tab's `Highlight` fill, keeping keyboard focus visually distinct. The
palette mapping is covered in the Windows WPF smoke test. A physical high-contrast
keyboard and screen-reader pass remains outstanding.

The Release build passes with zero warnings/errors. Core tests pass 502 with one
opt-in HTTPS fixture skipped, Presentation passes 237, App passes 10, and the
asset-pipeline Python suite passes 75. Documentation checks cover 43 authored
documents and 297 local links. Localization resources, prompt freshness, catalog
schema, starter hashes/size and `dotnet format --verify-no-changes` pass. Prompt
generation still emits its pre-existing
non-blocking suggestion that the Rocket League game pack add a light tone.

Keyboard-only operation, Narrator, live Windows high-contrast changes, 200–300% DPI,
mixed-DPI and hot-plug display arrangements, the Windows wallpaper-setting suite,
four-core hardware and the 24-hour soak were not run for this update. Existing
Oct 1 native COM and desktop restoration evidence remains in
[the prior review record](../REVIEW_2026-10-01.md) and its dated receipts.
Dedicated ultrawide and portrait renditions for the nineteen new default alternates
also remain a later art pass.

## Continued work on 2026-10-02

The Library cards now stretch to use available row space while retaining a 236-DIP
minimum and 336-DIP maximum. The [860×560 minimum capture](../../TestResults/ui-quality-2026-10-02/Main-Minimum-Library.png)
shows two comfortably sized game cards per row; the [normal-width capture](../../TestResults/ui-quality-2026-10-02/Library.png)
shows three. High-contrast navigation uses `HighlightText` for the keyboard focus
outline against the selected tab's `Highlight` fill; the WPF smoke test checks this
system-brush mapping and keeps focus distinct from selection.

Four further 1672×941 landscape masters add more choices to existing collections:
Mint Hills (Pastel), Basalt Columns at Dusk (Matte Black), Attic Window Moon (Cozy
Lo-fi) and Violet Ring in Fog (Neon Minimal). Their prompt-configured RealESRGAN x4
outputs were checked at 3840×2160 and 640×360, with the left icon and bottom taskbar
safe zones visible in the [review sheet](../../TestResults/art-2026-10-02-batch4/review/batch-contact-final.png).
Each contributes only a 16:9 rendition and thumbnail; dedicated ultrawide and
portrait versions remain unbuilt. Catalog `2026.10.02.6` validates with 258
referenced assets. The offline bundle is 123.60 MiB, leaving 4.40 MiB below its
128 MiB limit.

The updated coverage report records 211/286 landscape masters, 32/286 ultrawide
masters, 32/286 portrait masters, 205 built wallpapers and 186 catalog wallpapers.
There are now seventeen new default alternates across thirteen of the fourteen
collections; every collection retains one starter and offers at least two choices.
Four collections offer three wallpapers after this pass, and Liquid Glass already
had three. Artwork generation remains in progress.

Two more approved choices, Eucalyptus on Linen (Sage Botanical) and Mint Frost
Orbs (Soft Gradients), were generated as 1672×941 masters, upscaled through their
configured RealESRGAN x4 models, and reviewed at 3840×2160 and thumbnail size. The
[review contact sheet](../../TestResults/art-2026-10-02-batch6/review/approved-contact.png)
shows their focal points and safe zones. Catalog `2026.10.02.7` validates with 262
referenced assets; the offline bundle is 126.40 MiB with 1.60 MiB below its cap.
The app-matched focal-crop previews were also checked at 16:10 and 3:2 for all
six new choices; the subjects stay in frame and the left icon/taskbar bands remain
quiet ([16:10](../../TestResults/art-2026-10-02-crop-review/crop-preview-16x10.png),
[3:2](../../TestResults/art-2026-10-02-crop-review/crop-preview-3x2.png)).

Coverage is now 214/286 landscape masters, 32/286 ultrawide masters, 32/286
portrait masters, 208 built wallpapers and 188 catalog wallpapers. Nineteen new
default alternates span thirteen collections; all fourteen collections retain one
starter and at least one alternate, and seven now have three choices. A separate
Moss Macro candidate remains unapproved and outside the catalog because its lower
edge is too textured for the taskbar-safe band. Artwork generation remains in
progress.

General Settings, the Home monitor preview and the optional desktop surface now
enumerate monitors away from the WPF thread during topology changes, then apply
current snapshots through the dispatcher. Generation checks discard out-of-order
results; disposed settings view models ignore queued callbacks. General Settings
shows the primary-display fallback when its saved target is absent from the current
list without silently rewriting that preference. Presentation tests cover display-list
refresh, zero settings writes, stale selections and disposal; the WPF smoke provider would
delay 750 ms if called on the UI thread, while topology handling returns to the UI
in under 300 ms. Presentation passes 239 tests and App passes 10. The full Release solution
build completes with zero warnings; `check_docs.py` finds 43 authored documents and
298 local links. Physical hot-plug, mixed-DPI and high-DPI acceptance remain unrun.

One more reviewed alternate, Snowfield Haze (Clean White), adds a quiet winter
landscape without creating a category. Its 1672×941 master was built at 3840×2160
and 640×360; the combined rendition and thumbnail are 1.124 MiB. App-matched
focal-crop previews at 16:9, 16:10 and 3:2 show the subject clear of the desktop
icon and taskbar bands in the [review contact](../../TestResults/art-2026-10-02-cw04-review/crops-focal-70-46.png).
Catalog `2026.10.02.8` validates with 264 referenced assets; the 127.52 MiB offline
bundle leaves 0.48 MiB below its 128 MiB cap. The fourteen default collections now
contain 36 wallpapers: every collection has one starter, eight have three choices,
and six have two. Dedicated ultrawide and portrait versions for all twenty new
default alternates remain unbuilt. The refreshed generation report records 215/286
landscape masters, 32/286 ultrawide and 32/286 portrait masters, 209 built wallpapers
and 189 catalog wallpapers; work remains in progress. Only 0.48 MiB remains in the
bundle. A further addition would need to stay at or below 0.33 MiB to preserve the
reviewed 0.15 MiB reserve, so the remaining wallpaper work is deferred instead of
forcing a low-size render.

## Display refresh responsiveness follow-up

The first display-event pass moved hot-plug enumeration off the WPF dispatcher, but
review found synchronous reads still reachable through normal clock refreshes,
Home's initial load, onboarding plan rebuilds and user-image resolution guidance.
All presentation paths now query `IMonitorProvider` on background tasks and apply
results on the UI dispatcher. Home and onboarding expose a localized loading state;
generation checks discard stale snapshots, and disposal cancels or ignores queued
updates. Display changes rebuild onboarding download plans from the newest snapshot;
ordinary pack changes reuse the current snapshot. Game-detail downloads and image
imports await monitor reads without holding the dispatcher. Both successful and
failed Settings queries are generation-checked, so an older failure cannot replace
a newer display list. If a topology change replaces a query while image import is
waiting, the old wait is released as soon as the new query is published, even if
the old provider call is still blocked. The import follows the newest query and
leaves the live cache to that refresh's UI callback; a later stale completion cannot
replace the newer result.

When a successful onboarding query returns no usable display sizes, the display
summary, download summary and pack row all use the same unavailable-display guidance
instead of describing the condition as missing artwork or implying that no download
is needed. A failed re-query also hides stale download plans and disables their
rows while preserving the user's selection for a later retry. While a refresh is
pending, rows say that display information is updating and their checkboxes are
disabled. The loading flag is cleared before final row statuses are rebuilt, so a
row no longer remains stuck on "Detecting connected displays…" after the summary
has settled.

Settings no longer describes an absent saved ID as a confirmed disconnect or promises
that the clock is currently visible on a fallback screen. The Windows provider maps
some query failures to an empty list, so empty-state copy now describes unavailable
display information and preserves the saved selection without claiming what caused
the empty result. The desktop clock reuses its last snapshot for settings, system and
detection refreshes, querying again asynchronously only when no snapshot exists or
Windows reports a topology change.

The strengthened WPF smoke check records provider calls and UI-thread calls, holds a
750 ms penalty for any UI-thread read, and verifies both prompt event handling and
placement on the monitor returned by the refresh. Exercising the actual surface also
exposed a logical-parent error: the clock label was attached to a border and then
added to a stack without detaching it. The surface now builds its content tree before
assigning the parent, and the smoke test verifies visible/hidden lifecycle and native
placement. Presentation coverage includes a blocked initial Home query, blocked
onboarding query and an off-thread game-download query.

Validation after this pass: locked Release solution build, zero warnings/errors;
Core 502 passed and one opt-in HTTPS fixture skipped; Presentation 247 passed; App
10 passed; the read-only Windows COM monitor-context test passed. Documentation checks
found 43 authored documents and 300 local links with no errors; resource generation
and `dotnet format --verify-no-changes --no-restore` passed. The format command still
prints its existing generic workspace-load warning. The Windows wallpaper-changing
suite, physical hot-plug, mixed/high-DPI, Narrator and keyboard-only acceptance were
not run. The new smoke uses a synthetic second display; it does not establish native
hot-plug or multi-monitor hardware behavior.

The top-navigation review also checked the captured 860×560 minimum window: all
five tabs remain on one row, and the selected page remains identifiable. The light
and dark captures keep the selected state and labels legible. The WPF smoke asserts
selection follows tab choice and focus, verifies the Settings tab's keyboard stop,
accessible name and 44-pixel minimum height, and moves focus to About to verify
focus-driven selection. Manual keyboard-only and arrow-key use, Narrator,
high-contrast runtime, and physical DPI/mixed-monitor acceptance remain unrun.
