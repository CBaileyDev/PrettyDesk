# PrettyDesk

PrettyDesk changes your Windows 11 wallpaper to game-inspired art while you play, then goes back to your clean default (or a rotating
set) when you stop. It lives in the tray, needs no admin rights, and sends nothing anywhere.

> **Status:** feature-complete against [`docs/SPEC.md`](docs/SPEC.md) milestones M0-M6; the artwork itself is the owner's next step
> (all 286 prompts are written, see [`art/PROMPTS.md`](art/PROMPTS.md)), and M7 (soak and manual QA on real hardware) is still to be run.
> Windows behavior is proven only where CI says so; see [`docs/PERF.md`](docs/PERF.md) and [`docs/QA_CHECKLIST.md`](docs/QA_CHECKLIST.md).

## What it does
- **Game-aware.** Detects 45 games out of the box (new ones arrive through a signed online catalog, no app update needed). When a game
  starts, your desktop switches to a matching wallpaper; when it exits, your default returns. Add your own games in a few clicks.
- **Made for "clean setup" people.** Default collections match your desk: Matte Black, Clean White, Warm Minimal, Sage and Botanical,
  Aura Gradients, Misty Nature, Painted Landscapes, Steel Blue Night, Cozy Lo-fi, Deep Space, Pastel Dream, Neon Minimal, Architecture
  and Light. Every game also gets a **minimal** wallpaper so the vibe survives while you play.
- **Pixel-perfect on any screen.** Each monitor gets an image rendered at its exact resolution from the closest aspect ratio
  (16:9, 16:10, 3:2, 21:9, 32:9, portrait), cropped around the art's focal point. Mixed-DPI multi-monitor setups work.
- **Invisible.** A small tray app: idle CPU and memory targets are in [`docs/PERF.md`](docs/PERF.md), it survives sleep, lock, Explorer
  restarts and display changes, and "Restore my original wallpaper" puts back exactly what you had (also on uninstall).
- **Anti-cheat safe.** It only reads process names and never opens a process with more than `PROCESS_QUERY_LIMITED_INFORMATION`, never reads
  memory, never injects or hooks another process. A source guard test enforces this on every build.

## Install
Download the installer for your PC from the [latest release](https://github.com/CBaileyDev/PrettyDesk/releases/latest):
`PrettyDeskApp-win-x64-stable-Setup.exe` (Intel/AMD) or `PrettyDeskApp-win-arm64-stable-Setup.exe` (ARM). It installs for your user only,
with no admin prompt. Updates arrive automatically; a portable zip is attached too (no auto-update).

## Privacy
- **No telemetry, no accounts, no analytics.** Nothing about you or your games is ever sent.
- The running-process list never leaves your machine and is never written to disk; logs only contain matched game ids and have your user name removed.
- The only network traffic is fetching the signed wallpaper catalog and wallpaper packs from the content host, and checking GitHub
  Releases for updates. Requests carry the app version in the User-Agent and nothing else.
- Everything lives under `%LOCALAPPDATA%\PrettyDesk` (settings, downloaded packs, your images, your original-wallpaper backup, logs).
  Uninstalling asks whether to delete it.

## Build, test, check
```bash
dotnet build PrettyDesk.sln -c Release                         # zero warnings is enforced
dotnet test --project tests/PrettyDesk.Core.Tests -c Release   # Linux or Windows
dotnet test --project tests/PrettyDesk.Presentation.Tests -c Release
dotnet format PrettyDesk.sln --verify-no-changes
pip install pyyaml pillow numpy jsonschema pytest && python -m pytest tools/assetpipe   # asset pipeline + catalog schema
python3 tools/strings.py check && python3 tools/gen_notices.py --check                  # resource strings, third-party notices
```
`PrettyDesk.Core` and `PrettyDesk.Presentation` are pure .NET and run anywhere. `PrettyDesk.Windows` and `PrettyDesk.App` compile on
Linux (`EnableWindowsTargeting`) but run only on Windows; CI (`windows-latest`) builds both architectures, runs the Windows
integration tests, and starts the whole WPF app with zero binding errors.

## Repository map
| Path | What |
|---|---|
| `src/PrettyDesk.Core` | Detection, session tracking, rotation, orchestration, rendering, catalog and content (no Windows APIs) |
| `src/PrettyDesk.Windows` | CsWin32 interop: wallpaper COM, process snapshots, hooks, registry, backup/restore |
| `src/PrettyDesk.Presentation` | View models, resource strings, update policy (testable without WPF) |
| `src/PrettyDesk.App` | WPF shell, tray, onboarding, DI composition, Velopack |
| `tools/assetpipe` | Prompt linter and renderer, variant builder, review sheets, publishing |
| `tools/catalog-sign` | Generate keys, sign and verify the catalog |
| `art/prompts` | One YAML per wallpaper pack: the full prompts (13 default collections, 45 games) |
| `content/` | `catalog.src.json` (games, packs, collections) and its JSON schema |
| `docs/` | Spec, art direction, ADRs, QA checklist, performance record, releasing guide, backlog |

## For the owner
[`docs/RELEASING.md`](docs/RELEASING.md) lists everything to set up once (signing, content host, catalog key, starter set) and the steps for each release.
[`docs/BACKLOG.md`](docs/BACKLOG.md) lists what is deliberately not done yet, and why.

## Docs
[Spec](docs/SPEC.md) | [Art direction](docs/ART_DIRECTION.md) | [Seed game catalog](docs/GAME_CATALOG_SEED.md) | [Architecture decisions](docs/adr) | [Working rules](CLAUDE.md)

PrettyDesk is an independent fan project and is not affiliated with or endorsed by any game publisher. All wallpapers are original
artwork inspired by the mood, palette and genre of each game; they contain no logos, characters or official assets.
