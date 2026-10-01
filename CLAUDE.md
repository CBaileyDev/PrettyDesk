# CLAUDE.md — Working rules for PrettyDesk

You are building **PrettyDesk**, a Windows 11 tray app that switches the desktop wallpaper to game-inspired art while a game runs, and otherwise shows a default or rotating wallpaper. It ships as a signed installer EXE that has to work for any Windows 11 user.

## Read first, in this order
1. `docs/SPEC.md`: the product and engineering spec. It is the source of truth. Requirement IDs (`FR-DET-3`, `NFR-2`, …) are referenced in code comments, tests and commits.
2. `docs/ART_DIRECTION.md`: wallpaper aesthetics, the prompt format, and the gold-standard examples.
3. `docs/GAME_CATALOG_SEED.md`: the seed games. Their exe names and AppIDs are **unverified**.

If the spec is ambiguous, choose the option that best serves "works for every Windows 11 user, invisible, premium quality". Record the choice in `docs/adr/NNNN-title.md` (a short ADR). Don't silently deviate from a "decided" item in SPEC §5.1.

## Order of work
Follow SPEC §12: M0 → M7, with the art track running in parallel (A1 before M3, then A2). Keep each milestone in its own PR-sized set of commits, and have CI green before moving on.

## Quality bar (non-negotiable)
- **No stubs presented as done.** No `NotImplementedException`, no `TODO` in shipped code paths, and no fake data in UI that's meant to be real. If something is deferred, it goes in `docs/BACKLOG.md` with a reason.
- **Tests come with the code.** Every Core class gets unit tests in the same commit. Time-dependent logic uses `TimeProvider`, never `DateTime.Now`, `Task.Delay` with real time, or `Thread.Sleep`.
- **Builds have zero warnings** (`TreatWarningsAsErrors`), nullable is enabled, analyzers are on (`AnalysisLevel latest-recommended`), and `dotnet format` stays clean.
- **Anti-cheat safety (FR-DET-3) is absolute.** Never open a process handle beyond `PROCESS_QUERY_LIMITED_INFORMATION`, never read process memory, never inject or hook other processes' windows.
- **Never require admin**, and never write to HKLM or Program Files.
- **Never change user system settings** beyond the wallpaper itself and our own HKCU Run key. In particular, never touch `JPEGImportQuality`.
- **Privacy:** no telemetry, and never log or send process lists.
- **UI quality:** every async action has a loading state, every error has a human message, every list has an empty state, and the UI is keyboard-accessible with AutomationProperties set.
- **Verify, don't assume.** If you aren't sure about a Win32/COM signature, an exe name or a library API, check the docs (Microsoft Learn, CsWin32 metadata, the library README) before writing code that depends on it. Say what you verified in the commit message.

## Environment notes
- You may be working in a Linux container. `PrettyDesk.Core` and its tests MUST build and run on Linux (`net10.0`, no Windows APIs).
- The Windows and App projects target `net10.0-windows`. Set `<EnableWindowsTargeting>true</EnableWindowsTargeting>` so they *compile* on Linux, but they can only be **run and tested on Windows**. CI (`windows-latest`) is the authority for them. Don't claim Windows behavior works unless CI or a Windows run proved it. Otherwise, say so plainly and add it to the QA checklist.
- Commands: `dotnet build PrettyDesk.sln -c Release`, `dotnet test tests/PrettyDesk.Core.Tests`, `dotnet format --verify-no-changes`.

## Art/prompt work rules
- Prompts live in `art/prompts/{packId}.yaml`, following ART_DIRECTION.md §7 exactly. Write every prompt out in full, with no placeholders.
- **Never** put a game's name, character names, place names or faction names inside prompt text (ART_DIRECTION.md §5).
- Run the self-review checklist (ART_DIRECTION.md §8) on every prompt before committing, and regenerate `art/PROMPTS.md` with `assetpipe prompts`.
- Match the specificity and structure of the gold-standard examples in ART_DIRECTION.md §9.

## Commits
- Use Conventional Commits (`feat(core): rotation shuffle bag (FR-WP-4)`). Make small, focused commits that include their tests.
- Don't commit raw or generated images (`art/raw/`, `art/out/` are gitignored), secrets or signing keys.
