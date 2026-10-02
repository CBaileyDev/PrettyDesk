# Developer tool map

Run scripts from the repository root unless specified. Use the local Python
environment described in [DEVELOPMENT](../docs/DEVELOPMENT.md). Scripts which build,
assemble, sign, install or apply wallpapers have side effects; read their parameters
and the relevant workflow before running them.

| Tool | Purpose / output |
|---|---|
| [check_docs.py](check_docs.py) | Validate local Markdown file links in authored guides; no third-party dependencies |
| [review.ps1](review.ps1) | Full local validation gate; logs/captures under `TestResults/review-gate` |
| [package.ps1](package.ps1) | Self-contained unsigned beta installer/portable package under `dist` |
| [acceptance.ps1](acceptance.ps1) | Isolated real install/update/uninstall rehearsal; changes temporary app installation and wallpaper |
| [uninstall_ui_acceptance.ps1](uninstall_ui_acceptance.ps1) | Native keep/delete-data dialog acceptance against disposable data |
| [catalog_acceptance.ps1](catalog_acceptance.ps1) | Signed loopback HTTPS feed and tamper/unsigned tests; needs local signing configuration |
| [sign_content.ps1](sign_content.ps1), [catalog-sign](catalog-sign) | Catalog signing/verification CLI; private key stays outside checkout |
| [serve_catalog.py](serve_catalog.py) | Local content fixture server, not production hosting |
| [perf](perf) | Startup, switching and soak scripts; read [PERF](../docs/PERF.md) |
| [strings.py](strings.py) | `add`, `gen`, `check` for .resx and generated string accessors |
| [strings_bulk.py](strings_bulk.py) | Bulk string maintenance helper; review generated resource changes |
| [gen_notices.py](gen_notices.py) | Generate/check shipped third-party notices |
| [validate_catalog.py](validate_catalog.py), [verify_starter.py](verify_starter.py) | Schema validation; bundled file hashes and size budget |
| [assetpipe](assetpipe) | Python CLI: prompts/lint/status/build/review/publish/starter |
| [review_generated.py](review_generated.py), [review_companions.py](review_companions.py) | Contact sheets and generated-master review helpers |
| [build_generated.py](build_generated.py) | Incremental renditions from generated masters; does not replace visual approval |
| [assemble_content.py](assemble_content.py) | Assemble explicitly reviewed assets into catalog/bundle/host staging |
| [generation_status.py](generation_status.py) | Refresh actual generation coverage; never invent completion |
| [make_icons.py](make_icons.py) | Build app/tray icon assets |

The main assetpipe code is under [assetpipe/assetpipe](assetpipe/assetpipe), with
[tests](assetpipe/tests) and [pyproject.toml](assetpipe/pyproject.toml). Canonical layout
is defined in [paths.py](assetpipe/assetpipe/paths.py). Prompt lint is in
[lint.py](assetpipe/assetpipe/lint.py); generic prompts reject known game/subject names,
while the approved Rocket League pack can declare a limited set of named fan-art subjects.

`tools/bin`, `.venv`, `.pytest_cache`, `__pycache__`, `*.egg-info`, `bin` and `obj` are
generated tooling outputs. Do not treat them as source or delete them without checking
for active workflows. Public release instructions live in [RELEASING](../docs/RELEASING.md).
