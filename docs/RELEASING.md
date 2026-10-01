# Releasing PrettyDesk

Everything the owner has to set up once, and the steps for each release. The workflows are `ci.yml` (every PR), `release.yml`
(tag `v*`) and `content.yml` (manual, publishes the signed catalog). None of the secrets below belong in the repository.

## One-time setup

### 1. Catalog signing key (OWNER-DECISION: required before remote catalogs are trusted)
```bash
dotnet run --project tools/catalog-sign -c Release -- keygen --out catalog-private.key   # keep this file offline or in a secret
```
- Put the printed **public** key into `TrustedKeys.PublicKeysBase64` in `src/PrettyDesk.Core/Catalog/CatalogSecurity.cs` and ship that build.
  Until you do, remote catalogs are rejected and the app runs on its bundled snapshot (fail closed).
- Secret `CATALOG_SIGNING_KEY` = the base64 private key. Variable `CATALOG_PUBLIC_KEY` = the base64 public key (used to verify before upload).
- Key rotation: add the new public key to the list, ship, then start signing with the new key.

### 2. Content host (OWNER-DECISION, SPEC 6.3)
- Create a Cloudflare R2 bucket (or any S3-compatible store) on a custom domain, and set `contentBaseUrl` in `content/catalog.src.json`
  to `https://your.domain/v1/` (https, trailing slash).
- Repository variables: `CONTENT_BASE_URL` (the same URL, compiled into the app), `CONTENT_BUCKET`, `CONTENT_S3_ENDPOINT`.
  Secrets: `CONTENT_ACCESS_KEY_ID`, `CONTENT_SECRET_ACCESS_KEY`.
- For `assetpipe publish` on your machine, set `PRETTYDESK_UPLOAD_CMD`, for example `rclone copy {src} r2:prettydesk/v1/`.

### 3. Code signing (required for public releases)
Azure Trusted Signing (see SPEC 9). Secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `TRUSTED_SIGNING_ENDPOINT`,
`TRUSTED_SIGNING_ACCOUNT`, `TRUSTED_SIGNING_PROFILE`. `release.yml` refuses to ship a **stable** release unsigned; pre-releases build
unsigned with a warning (SmartScreen will warn). If you use an OV certificate instead, replace the signing step's `--signTemplate`.

### 4. Starter wallpapers (the offline set inside the installer)
After the first default collections are generated, approved and published (below), run `python -m assetpipe starter`, zip `content/starter/`
(flat, at most 60 MB) and host the zip somewhere the workflow can fetch. Variables: `STARTER_SET_URL`, `STARTER_SET_SHA256` (optional but recommended).
A stable release fails without it.

## Producing the art
1. `art/PROMPTS.md` has every prompt (regenerate with `python -m assetpipe prompts` from `tools/assetpipe` after any YAML change).
2. Generate in ChatGPT following the steps at the top of that file; save masters as `art/raw/{packId}/{wallpaperId}_{L|U|P}.png`.
3. `python -m assetpipe status`, then per pack: `build --pack ID`, `review --pack ID` (open `art/review/ID/index.html`), set `approved: true` in the YAML for what passes.
4. `python -m assetpipe publish --pack ID` updates `content/catalog.src.json`, stages and uploads the files. Commit the catalog. Then run the **content** workflow (dry run first) to sign and publish `catalog.json`.
5. Optional but recommended: install `realesrgan-ncnn-vulkan` (needs a GPU) for masters smaller than the target (ADR 0005).

## Every release
1. Tick `docs/QA_CHECKLIST.md` on the release candidate and record any performance numbers in `docs/PERF.md`.
2. `git tag v1.2.3` (or `v1.3.0-beta.1` for the beta channel) and push the tag. `release.yml` builds win-x64 and win-arm64, signs, runs `vpk pack`,
   and creates a **draft** GitHub Release with generated notes. Rehearse first with the workflow's manual run (it keeps the packages as artifacts and uploads nothing).
3. Download the installer from the draft on a clean machine and run the install, update and uninstall items of the QA checklist.
4. Publish the draft. Stable users receive it through the in-app updater (checked 30 s after start and every 12 hours).

## If something goes wrong
- A bad release: publish a higher version with the fix; Velopack does not downgrade by default.
- A bad catalog: fix `content/catalog.src.json` (bump `catalogVersion`), re-run the content workflow; clients never accept an older `catalogVersion`.
- A wrong game rule: fix the exe name in the catalog; no app update needed.
