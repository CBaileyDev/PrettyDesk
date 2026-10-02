# Releasing PrettyDesk

Everything the owner has to set up once, and the steps for each release. The workflows are `ci.yml` (every PR), `release.yml`
(tag `v*`) and `content.yml` (manual, publishes the signed catalog). None of the secrets below belong in the repository.

## One-time setup

### 1. Catalog signing key
The application now trusts the generated P-256 public key in `CatalogSecurity.cs`. Its private key is protected with
CurrentUser DPAPI in `%LOCALAPPDATA%/PrettyDeskSigning/catalog.private.dpapi`, outside the checkout.
`tools/sign_content.ps1` signs and verifies local staging without printing the private key. A valid, tampered, and unsigned
HTTPS feed has been exercised with `tools/catalog_acceptance.ps1`. The following key-generation procedure is for replacement keys:
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
`content/starter/` now contains the 13 reviewed default wallpapers and their thumbnails, within the 60 MB budget.
The release workflow validates this checked-in bundle using `tools/verify_starter.py`. An external replacement zip can still be
supplied with `STARTER_SET_URL` and `STARTER_SET_SHA256`; a stable release fails when no valid starter bundle is available.

## Producing the art
1. `art/PROMPTS.md` has every prompt (regenerate with `python -m assetpipe prompts` from `tools/assetpipe` after any YAML change).
2. Generate in ChatGPT following the steps at the top of that file; save masters as `art/raw/{packId}/{wallpaperId}_{L|U|P}.png`.
3. `python -m assetpipe status`, then per pack: `build --pack ID`, `review --pack ID` (open `art/review/ID/index.html`), set `approved: true` in the YAML for what passes.
4. `python -m assetpipe publish --pack ID` updates `content/catalog.src.json`, stages and uploads the files. Commit the catalog. Then run the **content** workflow (dry run first) to sign and publish `catalog.json`.
5. Optional but recommended: install `realesrgan-ncnn-vulkan` (needs a GPU) for masters smaller than the target (ADR 0005).

## Every release

For a local unsigned beta installer and portable ZIP, run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/package.ps1
```

The default is version `1.0.0-beta.1` for x64. Use `-Version 1.0.0-beta.2` for a subsequent beta or `-Runtime win-arm64` for ARM64.
Outputs are in `dist/releases/<version>/<runtime>/`; the publish directory is separate. The build includes the .NET desktop
runtime and bundled wallpapers, excludes acceptance-only code, and emits SHA-256 hashes. Double-click the Setup executable,
or extract the Portable ZIP and open `PrettyDesk.exe`. Keep the portable folder together. These packages are unsigned and
offline for remote content while `ContentBaseUrl` remains empty; they do not bypass the public stable-release signing gates.

1. Tick `docs/QA_CHECKLIST.md` on the release candidate and record any performance numbers in `docs/PERF.md`.
2. `git tag v1.2.3` (or `v1.3.0-beta.1` for the beta channel) and push the tag. `release.yml` builds win-x64 and win-arm64, signs, runs `vpk pack`,
   and creates a **draft** GitHub Release with generated notes. Rehearse first with the workflow's manual run (it keeps the packages as artifacts and uploads nothing).
3. Download the installer from the draft on a clean machine and run the install, update and uninstall items of the QA checklist.
4. Publish the draft. Stable users receive it through the in-app updater (checked 30 s after start and every 12 hours).

## If something goes wrong
- A bad release: publish a higher version with the fix; Velopack does not downgrade by default.
- A bad catalog: fix `content/catalog.src.json` (bump `catalogVersion`), re-run the content workflow; clients never accept an older `catalogVersion`.
- A wrong game rule: fix the exe name in the catalog; no app update needed.
