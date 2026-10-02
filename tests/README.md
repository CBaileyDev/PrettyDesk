# Test map and evidence limits

Commands are in [DEVELOPMENT](../docs/DEVELOPMENT.md). These projects use xUnit v3
with Microsoft.Testing.Platform, so use `dotnet test --project <path>`.

| Suite | Responsibility | Environment / side effects |
|---|---|---|
| [Core.Tests](PrettyDesk.Core.Tests) | Rules, session timing, rotation, rendering, signed catalog/content, persistence, privacy/source guards | Portable; temporary fixture files; live HTTPS case needs separate setup |
| [Presentation.Tests](PrettyDesk.Presentation.Tests) | View models, flows, formatting, resources, update policy and XAML binding checks | Portable; no WPF rendering proof |
| [Windows.Tests](PrettyDesk.Windows.Tests) | Native process/display state and wallpaper COM/backup restore | Interactive Windows desktop; wallpaper temporarily changes, cleanup restores |
| [App.Tests](PrettyDesk.App.Tests) | Real WPF startup, single instance and detection/app integration | Windows; disposable processes and captures |
| [assetpipe tests](../tools/assetpipe/tests) | Lint, prompt format, imaging, variants, publish and catalog schema | Python environment; generated test assets |

Keep native suites and installer/wallpaper acceptance sequential. Do not run competing
wallpaper writers during restore verification. Avoid using normal user data in fixtures;
acceptance builds must use the isolated roots documented in [RELEASING](../docs/RELEASING.md).

Time-dependent domain tests use `TimeProvider`, with fakes under each suite's Support
folder. Prefer behavior assertions: debounce/exit/priority, fail-closed trust and hash
checks, durable backup ordering, cancellation and clear failure states.

Passing suites does not prove every game's executable identifier, ARM64 execution,
installer UI, a 24-hour soak, mixed-display hardware behavior, or visual polish. Add
native/render evidence to the [QA matrix](../docs/QA_CHECKLIST.md) when actually run.
Keep dated receipts under [docs/evidence](../docs/evidence); use `TestResults` for
scratch logs/captures. The [review report](../docs/REVIEW_2026-10-01.md) is a dated result,
not a permanent assertion that future revisions pass.
