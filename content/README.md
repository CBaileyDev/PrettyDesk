# Catalog and content ownership

| Input / output | Role |
|---|---|
| [catalog.src.json](catalog.src.json) | Runtime catalog source: games/rules, packs, collections and asset paths/hashes |
| [catalog.schema.json](catalog.schema.json) | Structural validation contract |
| `starter` | Bundled offline wallpapers and game thumbnails; copied into app builds |
| `art/out/publish` | Generated host staging, not public hosting |

Detection support, installed-game discovery and wallpaper availability are separate
states. A thumbnail or catalog entry does not mean its full-resolution wallpaper is
bundled/downloaded. The current `contentBaseUrl` is empty: remote game packs are
unavailable in the EXE until a real host is configured or a larger offline bundle is built.

[CatalogService](../src/PrettyDesk.Core/Catalog/CatalogService.cs) verifies signatures
before accepting remote catalogs and retains fallback content. Asset integrity and
starter size checks are implemented by [verify_starter.py](../tools/verify_starter.py)
and [assetpipe](../tools/assetpipe/assetpipe). Never invent hashes, bypass trust checks
or weaken download/path protections to work around missing hosting.

After reviewing/building images, use [assemble_content.py](../tools/assemble_content.py)
with an explicit reviewed-ID list; inspect changes to catalog asset metadata, bundled
files and prompt provenance together. Then run:

```powershell
python tools/validate_catalog.py
python tools/verify_starter.py
```

For signing/publishing follow [RELEASING](../docs/RELEASING.md). Private signing keys
stay outside the checkout. The [update plan](../docs/updates/2026-10-01-aesthetics-and-personalization.md)
tracks offline/online delivery choices and clear unavailable-download behavior.
