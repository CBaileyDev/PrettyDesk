# 0001 — Test stack: Shouldly + xunit v3 on Microsoft.Testing.Platform

**Status:** accepted · **Date:** 2026-10-01 · **Relates to:** SPEC §5.1 ("FluentAssertions (or Shouldly)")

## Context
SPEC §5.1 allows FluentAssertions or Shouldly. FluentAssertions 8+ moved to a commercial licence for
non-OSS use; PrettyDesk may be monetised later (SPEC §13.4), so we avoid the licence risk.
.NET 10's `dotnet test` also no longer supports the VSTest bridge for xunit v3.

## Decision
- Assertions: **Shouldly** (MIT).
- Runner: xunit v3 on **Microsoft.Testing.Platform**, enabled by `global.json` (`"test": {"runner": "Microsoft.Testing.Platform"}`).
- Command is therefore `dotnet test --project tests/PrettyDesk.Core.Tests` (a bare directory argument is rejected by the SDK).

## Consequences
CLAUDE.md's `dotnet test tests/PrettyDesk.Core.Tests` becomes `dotnet test --project tests/PrettyDesk.Core.Tests`.
