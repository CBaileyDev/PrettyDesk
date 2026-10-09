# PrettyDesk

PrettyDesk changes your Windows 11 wallpaper to game-inspired art while you play, then goes back to your clean default (or a rotating
set) when you stop. It lives in the tray, needs no admin rights, and keeps your game activity local.

> **Status (pre-release):** all 14 default collections have bundled offline wallpapers and thumbnails. Game artwork/renditions remain incomplete; see the dated coverage in [`art/GENERATION_STATUS.json`](art/GENERATION_STATUS.json).
> The content host is not configured, so remote game packs cannot currently download in the EXE. Bundled defaults, including Tide/Bloom/Dusk, work offline.
> A signed HTTPS test catalog and real x64 installation, update, and uninstall restoration have passed locally.
> An unsigned local x64 beta installer and portable ZIP have been built. Public hosting, production signing, the interrupted 24-hour soak, and the broader hardware QA matrix remain incomplete;
> see [`docs/PERF.md`](docs/PERF.md), [`docs/QA_CHECKLIST.md`](docs/QA_CHECKLIST.md), and [`docs/BACKLOG.md`](docs/BACKLOG.md).

For source work, start with [AGENTS.md](AGENTS.md), the [documentation map](docs/README.md)
and [architecture](docs/ARCHITECTURE.md). The [next-update plan](docs/updates/README.md)
covers delivery, detection, themes, recognizable Rocket League fan art, taskbar options and widgets.

## What it does
- **Dashboard interface.** A flush sidebar, solid cards and one indigo accent in light, dark and high-contrast palettes, with no
  blur or transparency ([ADR 0016](docs/adr/0016-dashboard-visual-language.md)). An "Only dark wallpapers" switch keeps rotation, game
  pages and the picker to dark art. Three original glass-sculpture wallpapers (Tide, Bloom, Dusk) ship in all six supported screen
  ratios, ready offline. See [`docs/IOS_DESIGN.md`](docs/IOS_DESIGN.md).
- **Game-aware.** The catalog contains rules for 46 games; their exe names and AppIDs still need broader real-install verification (see [`docs/BACKLOG.md`](docs/BACKLOG.md)). Matching a running game selects its available wallpaper; when it exits, your default returns. Add your own games in a few clicks. A configured signed feed can update rules without an app update.
- **Made for "clean setup" people.** Default collections match your desk: Matte Black, Clean White, Warm Minimal, Sage and Botanical,
  Aura Gradients, Misty Nature, Painted Landscapes, Steel Blue Night, Cozy Lo-fi, Deep Space, Pastel Dream, Neon Minimal, Architecture
  and Light. Every game is planned to get a **minimal** wallpaper so the vibe survives while you play (not all are generated yet).
- **Per-monitor rendering.** Each monitor gets an image rendered at its resolution from the closest available aspect ratio
  (16:9, 16:10, 3:2, 21:9, 32:9, portrait), cropped around the art's focal point. The broader mixed-DPI/hardware matrix remains in QA.
- **Invisible.** A small tray app: idle CPU and memory targets are in [`docs/PERF.md`](docs/PERF.md), it survives sleep, lock, Explorer
  restarts and display changes. Restore supports pictures, solid colors, and saved slideshow sources and timing (also on uninstall).
  PrettyDesk preserves Windows Spotlight by asking you to select Picture or Slideshow before applying a wallpaper.
- **Non-invasive game detection.** It reads process names/PIDs and limited candidate path/window-title details, never opens a process with more than `PROCESS_QUERY_LIMITED_INFORMATION`, never reads
  memory, never injects or hooks another process. Source guard tests enforce the boundary in the test gate.

## Local beta packages
The current x64 outputs are under `dist/releases/1.0.0-beta.1/win-x64`:
`PrettyDeskApp-win-x64-beta-Setup.exe` and `PrettyDeskApp-win-x64-beta-Portable.zip`.
The installer is per-user and the packages are unsigned. This package build does not
establish a public signed release, a working remote wallpaper host, or ARM64 runtime validation.
See [RELEASING](docs/RELEASING.md) to build the next beta and verify installation/update/uninstall.

## Privacy
- **No telemetry, no accounts, no analytics.** Nothing about you or your games is ever sent.
- The running-process list never leaves your machine and is never written to disk; logs only contain matched game ids and have your user name removed.
- The only network traffic is fetching the signed wallpaper catalog and wallpaper packs from the content host, and checking GitHub
  Releases for updates. Requests carry the app version in the User-Agent and nothing else.
- Everything lives under `%LOCALAPPDATA%\PrettyDesk` (settings, downloaded packs, your images, your original-wallpaper backup, logs).
  Uninstalling asks whether to delete it.

## Build, test, check
See [DEVELOPMENT](docs/DEVELOPMENT.md) for setup, exact commands and generated-file rules.
On Windows, `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/review.ps1` runs the local review gate and saves logs, UI captures,
and a detection summary under `TestResults/review-gate`. Add `-PublishPlatforms` to check locked x64 and ARM64 ReadyToRun publishes.
The Windows tests briefly change the desktop wallpaper and restore it afterwards. The signed HTTPS fixture is a separate check:
`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/catalog_acceptance.ps1 -AllCoreTests` (requires the existing local signing key).

```bash
dotnet build PrettyDesk.sln -c Release                         # zero warnings is enforced
dotnet test --project tests/PrettyDesk.Core.Tests -c Release   # Linux or Windows
dotnet test --project tests/PrettyDesk.Presentation.Tests -c Release
dotnet format PrettyDesk.sln --verify-no-changes
pip install pyyaml pillow numpy jsonschema pytest && python -m pytest tools/assetpipe   # asset pipeline + catalog schema
python3 tools/strings.py check && python3 tools/gen_notices.py --check                  # resource strings, third-party notices
python3 tools/check_docs.py                                                           # authored navigation links
```
`PrettyDesk.Core` and `PrettyDesk.Presentation` are pure .NET and run anywhere. `PrettyDesk.Windows` and `PrettyDesk.App` compile on
Linux (`EnableWindowsTargeting`) but run only on Windows; CI (`windows-latest`) builds both architectures, runs the Windows
integration tests, and starts the whole WPF app with zero binding errors.

## Repository map
| Path | What |
|---|---|
| [src](src/README.md) / `PrettyDesk.Core` | Detection, session tracking, rotation, orchestration, rendering, catalog and content (no Windows APIs) |
| `src/PrettyDesk.Windows` | CsWin32 interop: wallpaper COM, process snapshots, hooks, registry, backup/restore |
| `src/PrettyDesk.Presentation` | View models, resource strings, update policy (testable without WPF) |
| `src/PrettyDesk.App` | WPF shell, tray, onboarding, DI composition, Velopack |
| [tools](tools/README.md) / `assetpipe` | Prompt linter and renderer, variant builder, review sheets, publishing |
| `tools/catalog-sign` | Generate keys, sign and verify the catalog |
| [art](art/README.md) / `prompts` | One YAML per wallpaper pack; authored prompts versus generated outputs |
| [content](content/README.md) | Catalog, schema and bundled offline content |
| [tests](tests/README.md) | Portable logic, native Windows and WPF verification |
| [docs](docs/README.md) | Architecture, spec, ADRs, evidence, workflows and update proposals |

## For the owner
[`docs/RELEASING.md`](docs/RELEASING.md) lists everything to set up once (signing, content host, catalog key, starter set) and the steps for each release.
[`docs/BACKLOG.md`](docs/BACKLOG.md) lists what is deliberately not done yet, and why.

## Docs
[Documentation index](docs/README.md) | [Spec](docs/SPEC.md) | [Art direction](docs/ART_DIRECTION.md) | [Seed game catalog](docs/GAME_CATALOG_SEED.md) | [Architecture decisions](docs/adr) | [Working rules](AGENTS.md)

PrettyDesk is an independent fan project and is not affiliated with or endorsed by any game publisher.
Existing packs use generic game-inspired compositions. The proposed [Rocket League fan-art pilot](docs/updates/rocket-league-fan-art.md)
uses recognizable named cars in new compositions, with distribution requirements checked separately before release.
