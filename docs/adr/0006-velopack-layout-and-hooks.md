# 0006. Velopack layout, install/uninstall hooks and update channels

- Status: accepted
- Relates to: SPEC §5.6, §9, FR-APP-3, FR-RESTORE-2

## Context
SPEC §5.6 puts user data in `%LOCALAPPDATA%\PrettyDesk\` as siblings of Velopack's `current\` folder, and §9 says the uninstall hook
"asks whether to keep user data". Two facts I could not verify from the build environment: what Velopack's uninstaller deletes inside
its root folder, and whether a hook that writes the Run key at install time is wanted (the onboarding quiz already asks about
"start with Windows"; FR-APP-3 says it is the user's choice).

## Decision
1. **Velopack pack id is `PrettyDeskApp`** (binaries under `%LOCALAPPDATA%\PrettyDeskApp\`), while user data stays in `%LOCALAPPDATA%\PrettyDesk\`.
   Data therefore survives updates *and* any wipe of Velopack's own folder, and "keep my data" is the safe default whatever the
   uninstaller does. The user-visible name (shortcut, installer title) is still "PrettyDesk" (`--packTitle`).
2. **No install-time Run key.** First-run onboarding asks and writes it (`StartWithWindows`, default on); `AppRuntime` only refreshes an
   existing entry, and only after onboarding is completed.
3. **Uninstall hook** (`OnBeforeUninstallFastCallback`, 30 s hard limit): remove the Run key, restore the original wallpaper from the
   backup, then show a native Yes/No box about deleting settings, downloaded wallpapers and the user's images. The default button is
   **No**; closing the box or running out of time keeps the data.
4. **Channels:** `stable` (GitHub releases that are not pre-releases) and `beta` (pre-releases), chosen by *Settings > General > Get beta versions*.
   Stable releases are packed for the `stable` channel only, so a beta user receives beta builds until they switch back.
5. **Update policy** lives in `UpdateCoordinator` (Presentation, unit-tested with a fake clock): check 30 s after start and every 12 h,
   download in the background, install at the next start or on "Restart to update"; failures are quiet and retried at the next check.

## Consequences
- Release workflow packs with `--packId PrettyDeskApp --packTitle PrettyDesk --channel stable|beta` for each RID.
- QA checklist must verify install, update (old to new) and uninstall on a clean VM, including what remains on disk afterwards.
- Velopack API surface used (`VelopackApp`, `UpdateManager`, `GithubSource`, `UpdateOptions.ExplicitChannel`, `OnBeforeUninstallFastCallback`)
  was checked against the 1.2.161 package's XML documentation, not run: updating and hooks cannot execute on the Linux build environment.
