# Documentation map

Start with [AGENTS.md](../AGENTS.md) for working constraints or
[DEVELOPMENT.md](DEVELOPMENT.md) to build and test. Documents below have different
roles: requirements describe intent; source maps describe implementation; dated
evidence describes only what was actually tested.

## Current implementation and workflows

| Document | Use it for |
|---|---|
| [Architecture](ARCHITECTURE.md) | Ownership, dependency direction and runtime flows |
| [Development](DEVELOPMENT.md) | Setup, exact commands, generated files and checks |
| [Source map](../src/README.md) | Files to inspect for a feature or bug |
| [Tests](../tests/README.md) | Suite responsibilities and native test side effects |
| [Tools](../tools/README.md) | Validation, packaging, signing, art and performance scripts |
| [Art](../art/README.md) / [Content](../content/README.md) | Authored inputs, build outputs and offline versus hosted assets |
| [Releasing](RELEASING.md) | Local unsigned beta and public signed release procedures |

## Requirements, design and unfinished work

| Document | Status / purpose |
|---|---|
| [SPEC](SPEC.md) | v1.0 baseline; retain requirement IDs and recorded decisions |
| [Art direction](ART_DIRECTION.md) | Production quality, prompt format and scoped fan-art direction |
| [Game catalog seed](GAME_CATALOG_SEED.md) | Seed research; identifiers need install verification |
| [iOS design](IOS_DESIGN.md) | Existing Liquid Glass refresh and three-wallpaper capsule |
| [ADRs](adr) | Accepted architecture decisions, numbered 0001 onward |
| [Backlog](BACKLOG.md) | Deferred work and release gaps |
| [Update proposals](updates/README.md) | Next-update scope, choices and acceptance conditions |

## Verification records

- [Review, 2026-10-01](REVIEW_2026-10-01.md): reviewed code, local checks and explicit limits.
- [Performance](PERF.md): measured samples and outstanding soak requirements.
- [QA checklist](QA_CHECKLIST.md): native installation, display, accessibility and game matrix.
- [Dated evidence](evidence/2026-10-01): preserve original receipts; do not rewrite an
  interrupted run as a pass.

The content host is not configured. The local beta EXE exists; it is unsigned.
The interrupted 24-hour soak, public signing/hosting and wider hardware validation
remain release gaps. A successful build does not close those gaps.
