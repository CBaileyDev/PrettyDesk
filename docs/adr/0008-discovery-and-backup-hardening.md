# 0008: Epic bootstrapper discovery and durable wallpaper backup

Date: 2026-10-01

The live scanner found no catalog games on a machine with Epic installations of Rocket League, Fortnite and Cyberpunk 2077.
The manifests declare `Launcher.exe`, `FortniteBootstrapper.exe` and `redprelauncher.exe`; the gameplay process rules use different names.

Installed-game discovery now also joins complete primary Epic manifests to exact catalog display titles, ignoring trademark marks,
case and surrounding whitespace. It excludes manifests without an EXE launch target or an existing installation folder. It does not
use substring title matching, so DLC and bonus-content names do not label a base game as installed. This fallback is for installed
chips and prefetch only. Running-game detection continues to use the existing process/Steam rules. Localized or renamed titles can
still need a custom game or a future explicit Epic identifier mapping.

Steam discovery supports both library-folder layouts, isolates failures per library/manifest and still scans the primary library
when the library-folder file is locked. Launcher metadata is limited to 1 MiB per file to avoid accidentally reading enormous files.

The original wallpaper metadata must reach disk before any PrettyDesk wallpaper replaces it. A failed state save now aborts backup
preparation with a human-readable error. The in-memory snapshot is retained and the next attempt retries persistence; it never captures
an already-replaced wallpaper as a new original. Tests exercise the initial failure, repeated failure and recovery after storage becomes writable.

Signed catalogs, settings and state now reject null sections/entries safely. Corrupt settings/state are preserved by the existing
recovery mechanism. Catalog/signature transfers have byte limits and complete-transfer deadlines; unsigned response padding cannot
consume unbounded memory. Asset transfers are capped at the schema's 8 MiB maximum, including when a declared size is absent, and
time out after two minutes per attempt. Concurrent requests for a pack share one lazily created task to prevent duplicate writers.

The specification's exe-only fallback when protected-process path lookup is denied remains unchanged. It can misidentify ambiguous
executables such as Path of Exile; this is a known detection policy tradeoff, not a reason to request stronger process permissions.
