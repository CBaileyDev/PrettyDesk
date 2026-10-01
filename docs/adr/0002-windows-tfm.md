# 0002 — Windows TFM `net10.0-windows10.0.19041.0`

**Status:** accepted · **Date:** 2026-10-01 · **Relates to:** SPEC NFR-8

## Decision
`PrettyDesk.Windows` and `PrettyDesk.App` target `net10.0-windows10.0.19041.0` (Windows 10 2004 SDK projection),
with `EnableWindowsTargeting=true` so they compile on Linux. The Windows 11 floor (build 22000) is enforced at
runtime by a startup check rather than by the TFM, because Windows 10 22H2 is best-effort (SPEC §1.2) and
CsWin32 / WPF-UI do not need the 22000 SDK.
