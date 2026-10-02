# PrettyDesk: agent entry point

PrettyDesk is a Windows 11 WPF tray application. It detects games locally and
applies static wallpapers per monitor. Start here; do not scan generated images,
build outputs, or private user data to understand the application.

## Read and route

1. [Documentation index](docs/README.md): choose the document for the task.
2. [Architecture](docs/ARCHITECTURE.md): ownership, startup and data flow.
3. [Development](docs/DEVELOPMENT.md): exact build, test and regeneration commands.
4. [Baseline specification](docs/SPEC.md): requirement IDs and engineering constraints.

| Task | Start with |
|---|---|
| Detection, session priority, installed-game discovery | [Source map](src/README.md), Core/Detection, Windows/ProcessSource and watchers |
| Wallpaper selection, rendering, restore | Core/Orchestration, Core/Imaging, Windows/DesktopWallpaperService and WallpaperBackupService |
| WPF appearance or interaction | Presentation/ViewModels, App/Views, App/Resources/Styles.xaml, App/Services/AppAppearance.cs |
| Catalog, downloads, availability | [Content guide](content/README.md), Core/Catalog and Core/Content |
| Prompts or assets | [Art guide](art/README.md), [art direction](docs/ART_DIRECTION.md), [tool map](tools/README.md) |
| Tests, release or performance evidence | [Test guide](tests/README.md), [releasing](docs/RELEASING.md), [review record](docs/REVIEW_2026-10-01.md) |
| Planned aesthetics, Rocket League, taskbar or widgets | [Update proposal](docs/updates/2026-10-01-aesthetics-and-personalization.md) |

The current user request takes precedence. Proposal documents distinguish approved
direction from unimplemented features and unresolved decisions; a proposal is not
evidence that its behavior ships. Preserve baseline IDs. Record material design
decisions in [docs/adr](docs/adr) rather than silently changing a decided requirement.

## Working constraints

- Preserve unrelated changes, downloaded masters, output assets, evidence and user
  settings. This checkout may have no Git metadata; check before issuing Git commands.
- Keep Core and Presentation portable (`net10.0`). Win32 belongs in Windows;
  WPF, application lifetime and composition belong in App.
- No administrator requirement, HKLM or Program Files writes. Baseline system
  changes are wallpaper and our own HKCU Run entry. Taskbar styling is a proposal
  requiring a scoped decision and restoration design; do not infer permission to
  modify the current machine's appearance from a documentation task. Never change
  `JPEGImportQuality`.
- Never request process access above `PROCESS_QUERY_LIMITED_INFORMATION`, read
  process memory, inject, or hook another process's windows. Do not weaken source guards
  for detection, widgets or taskbar experiments.
- No telemetry or logged/exported process lists. Keep local installation paths out
  of committed research reports. Never read, print, commit or copy signing private keys.
- Builds must have zero warnings, with nullable and analyzers enabled. No shipped
  stubs or fake success states. Explain deferred work in [BACKLOG](docs/BACKLOG.md).
- Use `TimeProvider` and deterministic tests for time-dependent logic. Add meaningful
  behavior tests for logic changes; documentation and cosmetic changes need appropriate
  validation, not tests that merely repeat their implementation.
- Preserve native dialogs, tray lifecycle, keyboard access, system theme and high
  contrast. Set appropriate AutomationProperties on UI controls. Async actions
  need loading, error and empty states.
- Verify unfamiliar APIs and detection identifiers against primary sources or real
  installs. Build success is not proof of native Windows behavior or game recognition.
- Treat locked dependency files as source. Refresh the normal, RID and ReadyToRun
  graphs deliberately when changing dependencies; do not disable locked restore.

## Fast checks (from repository root)

```powershell
python tools/check_docs.py
dotnet build PrettyDesk.sln -c Release -p:RestoreLockedMode=true
dotnet test --project tests/PrettyDesk.Core.Tests -c Release --no-build
dotnet test --project tests/PrettyDesk.Presentation.Tests -c Release --no-build
dotnet format PrettyDesk.sln --verify-no-changes --no-restore
```

Use `tools/.venv/Scripts/python.exe` when the local Python environment exists.
The [development guide](docs/DEVELOPMENT.md) covers native tests and the full gate;
Windows wallpaper tests affect the desktop temporarily and must run serially.

## Authored versus generated

- Edit prompt YAML, not `art/PROMPTS.md`; regenerate with `assetpipe prompts`.
- Edit `Strings.resx`, then run `python tools/strings.py gen` for its generated accessor.
- Review assembled catalog changes and hashes together. Do not hand-invent asset
  metadata or claim ungenerated variants are complete.
- Current prompt lint enforces generic art. The owner has approved named Rocket League
  fan art; its [draft prompts and migration](docs/updates/rocket-league-fan-art.md)
  supersede the old blanket ban for that direction, but the pipeline change is pending.
- Exclude `bin`, `obj`, `dist`, `TestResults`, `.venv`, caches, `art/raw`, `art/out`
  and `art/review` from broad searches. These may hold valuable local files; exclusion
  is not deletion permission. Do not commit raw/generated art or secrets.

After changes, report what changed, what ran, and any material tests still unrun.
Keep dated evidence intact. If Git is available, use small Conventional Commit changes
with relevant requirement IDs; do not create a new repository merely to satisfy this guide.
