# Build, test and maintain PrettyDesk

Run commands from the repository root. Use Windows 11 with an interactive desktop
for native tests. [global.json](../global.json) selects .NET 10 and the
Microsoft.Testing.Platform runner. PowerShell 7 and Python 3.12 are used by local
tooling/CI. Core and Presentation tests are portable; Windows/App tests require Windows.

## Repository controls

| File / folder | Controls |
|---|---|
| [PrettyDesk.sln](../PrettyDesk.sln) | Four app layers, their test projects and catalog signer |
| [Directory.Build.props](../Directory.Build.props) | Shared target/build analysis and lock-file policy |
| [global.json](../global.json) | SDK selection and test runner |
| [.editorconfig](../.editorconfig) | Source formatting and analyzer conventions |
| [.github/workflows](../.github/workflows) | Portable/native CI, content and release workflows |
| [.gitignore](../.gitignore) | Build, artwork, cache and secret exclusions |

Use [AGENTS.md](../AGENTS.md) for shared agent rules; `CLAUDE.md` points there
instead of duplicating policy. Folder READMEs map source, tests, tools, art and content.

## Setup

```powershell
dotnet --info
dotnet restore PrettyDesk.sln --locked-mode
python -m venv tools/.venv
tools/.venv/Scripts/python.exe -m pip install -e tools/assetpipe pytest jsonschema
```

If `tools/.venv` already exists, use it. The assetpipe package declares its image/YAML
dependencies in [pyproject.toml](../tools/assetpipe/pyproject.toml). On Linux use
`tools/.venv/bin/python` instead. Python is needed for developer tooling, not the EXE.

## Select the relevant check

```powershell
tools/.venv/Scripts/python.exe tools/check_docs.py
dotnet build PrettyDesk.sln -c Release -p:RestoreLockedMode=true
dotnet test --project tests/PrettyDesk.Core.Tests -c Release --no-build
dotnet test --project tests/PrettyDesk.Presentation.Tests -c Release --no-build
dotnet format PrettyDesk.sln --verify-no-changes --no-restore
```

For Windows integration, run the next two suites **serially** with the app and
other wallpaper-changing software stopped. The wallpaper tests temporarily change
the desktop and restore in cleanup; App tests launch disposable instances.

```powershell
dotnet test --project tests/PrettyDesk.Windows.Tests -c Release --no-build
dotnet test --project tests/PrettyDesk.App.Tests -c Release --no-build
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/review.ps1 -PublishPlatforms
```

The review gate covers documentation links, build, four suites, format, Python tests,
resources, notices, catalog schema, starter hashes, prompt drift and dependency audit.
`-PublishPlatforms` additionally validates locked x64/ARM64 ReadyToRun publishes.
Logs/captures are under `TestResults/review-gate`; a build on x64 is not ARM64 runtime proof.
The [test guide](../tests/README.md) explains evidence boundaries.

## Run and package

```powershell
dotnet run --project src/PrettyDesk.App -c Release
dotnet tool install vpk --version 1.2.161 --tool-path tools/bin
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/package.ps1 -Version 1.0.0-beta.2 -Runtime win-x64
```

Running the app uses normal per-user settings and may change the wallpaper. Package
creation does not install it. Skip packager installation if the pinned tool already
exists. Packages go to `dist/releases/<version>/<runtime>`; they are local unsigned
betas. Follow [RELEASING](RELEASING.md) for public signing and install/update/uninstall
acceptance. Do not ship acceptance builds.

The signed HTTPS fixture is separate from the default suite and needs the existing
local signing configuration:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/catalog_acceptance.ps1 -AllCoreTests
```

Do not generate or replace signing keys to make a check pass. Private keys are outside
this checkout and must remain private. Hosting requires an actual configured HTTPS
content feed; an empty `contentBaseUrl` does not become usable by packaging the EXE.

## Edit source inputs and regenerate outputs

| Change | Authored input | Regenerate / validate |
|---|---|---|
| UI strings | [Strings.resx](../src/PrettyDesk.Presentation/Resources/Strings.resx) | `python tools/strings.py gen`, then `check` |
| Pack prompts | [art/prompts](../art/prompts) | From `tools/assetpipe`: `python -m assetpipe prompts`, then `prompts --check` |
| Asset renditions | Reviewed masters in `art/raw` | [Art workflow](../art/README.md), build then visually review all intended crops |
| Catalog entries | [catalog.src.json](../content/catalog.src.json) and approved assembled assets | `python tools/validate_catalog.py`, `python tools/verify_starter.py` |
| Dependency notices | Restored .NET dependency graph | `python tools/gen_notices.py`, then `--check` |
| Doc links | Authored Markdown | `python tools/check_docs.py` |

Use the environment's Python executable in these examples. Generated files remain
derived from their authored inputs; editing a derived file alone leaves drift.
Named Rocket League fan-art drafts are currently in [update docs](updates/rocket-league-fan-art.md),
because existing prompt lint still enforces generic game inspiration.

## Navigate efficiently

```powershell
rg --files src tests -g '!**/bin/**' -g '!**/obj/**'
rg -n 'ActiveChanged|SetActiveGame' src -g '*.cs' -g '!**/obj/**'
rg --files docs tools -g '!**/.venv/**' -g '!**/bin/**' -g '!**/obj/**' -g '!**/__pycache__/**' -g '!**/*.egg-info/**'
```

Keep `dist`, `TestResults`, `bin/obj`, tooling environments and art outputs out of
source discovery. Preserve valuable local output/master files rather than moving or
deleting them as cleanup. Keep dated evidence separate from current scratch output.
