# PrettyDesk

PrettyDesk automatically changes your Windows 11 wallpaper to game-inspired art while you play, then goes back to your clean default (or a rotating set) when you stop.

> **Status:** under active construction, following [`docs/SPEC.md`](docs/SPEC.md) milestone by milestone (see the table below).

- 🎮 Detects 40+ games, with more added through a signed online catalog without app updates
- 🖼️ Renders at each monitor's exact resolution, from 1080p to 4K, 16:10, 3:2, ultrawide and portrait
- 🔁 Rotates through curated "clean setup" collections: Matte Black, Clean White, Warm Minimal, Pastel, Neon and more
- 🪶 Tiny tray app with no admin rights, no telemetry, and anti-cheat safe (it never touches game processes beyond reading the process name)

## Build & test
```bash
dotnet build PrettyDesk.sln -c Release
dotnet test --project tests/PrettyDesk.Core.Tests -c Release   # runs on Linux and Windows
dotnet format PrettyDesk.sln --verify-no-changes
```
`PrettyDesk.Core` is pure .NET and builds/tests anywhere. `PrettyDesk.Windows` and `PrettyDesk.App` compile on Linux
(`EnableWindowsTargeting`) but can only be run on Windows; CI (`windows-latest`) is the authority for them.

## Docs
- [Product & engineering spec](docs/SPEC.md)
- [Art direction & prompt guide](docs/ART_DIRECTION.md)
- [Seed game catalog](docs/GAME_CATALOG_SEED.md)
- [Architecture decisions](docs/adr)
- [Agent working rules](CLAUDE.md)

PrettyDesk is an independent fan project and is not affiliated with or endorsed by any game publisher. All wallpapers are original artwork.
