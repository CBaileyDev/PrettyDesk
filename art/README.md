# Artwork inputs and workflow

[ART_DIRECTION](../docs/ART_DIRECTION.md) defines composition, quality and the YAML
format. [Rocket League fan-art drafts](../docs/updates/rocket-league-fan-art.md) record
the owner's new direction. Four selected Rocket League pack landscapes are reviewed,
built and included in the offline bundle, including three named-car scenes; see the
[scoped prompt-mode record](../docs/updates/2026-10-02-rocket-league-prompt-mode.md).
Twenty new default alternates now fill sparse shelves across the fourteen existing
collections. Every collection has at least two wallpapers and exactly one starter;
eight now offer three choices, including Clean White's Snowfield Haze. No category
was added. The selection and offline-size tradeoff are recorded in the
[Oct 2 implementation note](../docs/updates/2026-10-02-top-navigation-and-default-art.md).

| Location | Ownership |
|---|---|
| [prompts](prompts) | Authored YAML per pack: full prompts, role/tone, focal point, approval and provenance |
| [PROMPTS.md](PROMPTS.md) | Generated copy/paste document; regenerate rather than hand-edit |
| [generation-queue.json](generation-queue.json) | Planned generation work, not evidence of completed images |
| [GENERATION_STATUS.json](GENERATION_STATUS.json) | Generated dated coverage report; refresh after actual generation/build work |
| `raw/<pack>/<wallpaper>_L.png`, `_U.png`, `_P.png` | Valuable local landscape, ultrawide and portrait masters; excluded from Git |
| `out/<pack>` | Built renditions/manifests, derived from masters |
| `review` | Generated contact sheets and review output |
| `out/publish` | Staged hosted content; not a live deployment |

The pipeline resolves the repository root through `PrettyDesk.sln`; moving masters,
renaming IDs or rearranging output folders without updating tooling breaks provenance.
Keep existing approved masters and catalog hashes intact during documentation changes.

From `tools/assetpipe`, using the configured Python executable:

```powershell
python -m assetpipe prompts --check
python -m assetpipe status
python -m assetpipe lint --pack game.rocket-league
```

For actual art work: author YAML, regenerate `prompts`, generate masters, build the
selected pack, render its review sheet, inspect each intended crop, record approval
and hashes, then assemble reviewed content. See [tool ownership](../tools/README.md)
and [content ownership](../content/README.md). A landscape master alone is not proof
that dedicated ultrawide/portrait art is finished or that the pack is available online.

Generic inspiration remains the default. Rocket League opts into named-fan-art
with a reviewed allowlist and an explicit `namedSubjects` selection; generic packs
continue to reject those subjects. All other lint checks remain active. Dedicated
ultrawide/portrait masters and public distribution-rights review remain separate
from the local landscape bundle.
