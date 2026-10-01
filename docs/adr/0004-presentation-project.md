# 0004 — Add `PrettyDesk.Presentation` (view models without WPF)

**Status:** accepted · **Date:** 2026-10-01 · **Relates to:** SPEC §5.2 (solution layout), §11 (testing), CLAUDE.md ("every Core class gets tests")

## Context
SPEC §5.2 puts view models in `PrettyDesk.App` (net10.0-windows). That project cannot be run or tested on the Linux
development/CI environment, so the logic behind every screen (status text, filtering, onboarding, settings binding,
error handling) would ship without automated tests, and XAML typos only surface on a Windows machine.

## Decision
Add `src/PrettyDesk.Presentation` (`net10.0`, no WPF, no Windows APIs) holding the view models, status formatters, the
`.resx` string table and the service interfaces the UI needs (dialogs, file pickers, launching URLs…). It depends on
`PrettyDesk.Core` and CommunityToolkit.Mvvm only. `PrettyDesk.App` keeps views (XAML), converters, tray, hosting and the
Windows implementations of those interfaces.

`tests/PrettyDesk.Presentation.Tests` runs on Linux and Windows. A Windows-only `PrettyDesk.App.Tests` instantiates every
view against fake view models and fails on WPF binding errors.

## Consequences
One more project than SPEC §5.2 lists; every other decided item in SPEC §5.1 is unchanged.
