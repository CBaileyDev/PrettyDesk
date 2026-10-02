# PrettyDesk — Product & Engineering Specification

> **Audience:** implementing engineers, agents and the project owner.
> **Status:** v1.0 spec, 2026-10-01.
> **Scope:** baseline requirements, not a completion report. See [current architecture](ARCHITECTURE.md),
> [dated review evidence](REVIEW_2026-10-01.md) and [next-update proposals](updates/README.md).
> **Companion docs:**
> - [`ART_DIRECTION.md`](./ART_DIRECTION.md): wallpaper aesthetics, prompt-writing rules, prompt file format
> - [`GAME_CATALOG_SEED.md`](./GAME_CATALOG_SEED.md): first game list, detection hints and art direction for each game
> - [`../AGENTS.md`](../AGENTS.md): shared working rules and repository navigation

Requirement keywords: **MUST** means required for v1.0. **SHOULD** means required unless there's a documented reason not to. **MAY** marks an optional or stretch item. Requirements have IDs (e.g. `FR-DET-3`) so commits, tests and PRs can reference them.

---

## 1. Product overview

**PrettyDesk** is a lightweight Windows 11 tray app. When it detects that a supported game is running, it switches the desktop wallpaper to a high-quality wallpaper inspired by that game. When the game closes, it returns to the user's chosen default: one fixed wallpaper, or a rotating set from curated "clean setup" collections.

### 1.1 Goals
1. **It just works** on any Windows 11 PC: x64 and ARM64, any monitor layout (1080p to 4K, 16:10 laptops, 3:2 Surface, 21:9/32:9 ultrawide, portrait), no admin rights, and no runtime the user has to install first.
2. **Premium visual quality.** Every wallpaper is rendered at each monitor's exact native resolution and aspect ratio. Nothing is stretched, blurry or double-compressed.
3. **Invisible when running.** CPU use is close to zero, there's no flicker and no wallpaper thrashing, and it never interferes with games or anti-cheat.
4. **Scales with content.** New games and wallpapers ship through a signed online catalog without a new app release.
5. **Aimed at the "clean setup" crowd.** Default collections match common desk-setup aesthetics (all-black, all-white, warm wood/Japandi, pastel, subtle RGB).

### 1.2 Non-goals (v1)
- Animated, video or live wallpapers (Wallpaper Engine territory). This MAY be considered for v2.
- In-game overlays, or reading or modifying game memory or files.
- macOS and Linux. Windows 10 is best-effort: it isn't blocked, but it isn't in the test matrix.
- User accounts, cloud sync, or a paid store. The architecture MUST NOT preclude adding these later.

### 1.3 Glossary
| Term | Meaning |
|---|---|
| **Game** | A catalog entry with detection rules and an associated **pack**. |
| **Pack** | A versioned group of wallpapers (a game pack or a default collection). |
| **Wallpaper** | One artwork, with several **variants**. |
| **Variant** | One aspect-ratio rendition of a wallpaper (`16x9`, `21x9`, ...). |
| **Context** | The current source of wallpapers: `Default` or `Game(<id>)`. |
| **Rotation** | Cycling through several wallpapers within a context on a timer. |

---

## 2. Core user flows

1. **First run (onboarding):**
   welcome → "What does your setup look like?" (Matte Black / Clean White / Warm Wood & Plants / Pastel / RGB / Surprise me) → default mode (single wallpaper or rotate every N) → "Games we found on this PC" (installed games detected locally, each with a toggle) → "Start with Windows" (on by default) → done. The app minimizes to the tray and shows a one-time tip.
2. **Day to day:** The user launches a game. About 3 seconds later the wallpaper changes to that game's art. The user quits, and about 10 seconds later the wallpaper goes back to the default or rotation, picking up where it left off.
3. **Customize:** Tray → Open PrettyDesk → Library. From there the user can toggle games, pick favorite wallpapers per game, preview one on the desktop, and change the rotation interval.
4. **Unlisted game:** Library → "Add a game" → pick it from the list of running apps (or browse to an .exe) → choose wallpapers from their own images or from any pack.
5. **Leave:** Settings → "Restore my original wallpaper", or uninstall. Either one puts back the exact wallpaper(s) the user had before PrettyDesk.

---

## 3. Functional requirements

### 3.1 Game detection (`FR-DET`)
- **FR-DET-1** MUST detect running games by matching catalog rules against running processes. Rule types:
  - `exeNames`: case-insensitive exact file-name match (the primary rule).
  - `steamAppIds`: matched against `HKCU\Software\Valve\Steam\RunningAppID` (a DWORD, 0 when nothing is running). Watch it with `RegNotifyChangeKeyValue` rather than polling.
  - `pathContains`: optional substring match on the full image path. Use it only when the exe name is ambiguous.
  - `windowTitleContains`: optional check on the visible top-level window title of the matched process, for ambiguous hosts such as `javaw.exe` (Minecraft Java).
  - `excludeExeNames`: launchers and helpers that must never count (e.g. `steam.exe`, `EpicGamesLauncher.exe`, `RiotClientServices.exe`).
  A game matches when at least one positive rule matches **and** every secondary rule present on that game is satisfied.
- **FR-DET-2** Process enumeration MUST use `CreateToolhelp32Snapshot`/`Process32NextW` through CsWin32 (no process handles needed) on a **2 s** interval, configurable from 1 to 10 s. It MUST also react immediately to foreground-window changes through `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`, so the active game is resolved without waiting for the next poll.
- **FR-DET-3 (anti-cheat safety, non-negotiable):** The app MUST NOT open a game process with anything beyond `PROCESS_QUERY_LIMITED_INFORMATION`, and only when a rule needs the image path. It MUST NOT inject, hook game windows, read process memory, enumerate modules, read game files, or install drivers. Access-denied results (protected processes) MUST be handled silently: fall back to the exe-name-only match.
- **FR-DET-4 Debounce:** a game becomes *active* only after it has matched continuously for `detectDelay` (default **3 s**). This avoids switching on crash-on-launch or launcher hand-offs.
- **FR-DET-5 Exit grace:** after the last matching process for the active game disappears, keep the game context for `exitGrace` (default **10 s**) before reverting. This covers games that restart themselves, such as after a patch or a settings change.
- **FR-DET-6 Multiple games:** if more than one enabled game is active, the one most recently in the **foreground** wins, with the most recent start time as the tie-breaker.
- **FR-DET-7** Users MUST be able to disable detection per game, and globally with "Pause".
- **FR-DET-8 (SHOULD)** Unknown-game hint: when `SHQueryUserNotificationState` reports `QUNS_RUNNING_D3D_FULL_SCREEN` or `QUNS_BUSY` for 60 s or longer and the foreground process isn't in the catalog, offer "Add *<exe>* as a game?" once per exe through a tray notification. This is opt-out in settings.
- **FR-DET-9 Installed-game discovery** (for onboarding and prefetch) MUST be local-only and read-only:
  - Steam: `libraryfolders.vdf` + `appmanifest_*.acf` (find the Steam path through `HKCU\Software\Valve\Steam\SteamPath`).
  - Epic: `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON).
  - Riot, Battle.net and Xbox/MS Store MAY come later. Missing launchers MUST NOT cause errors.

### 3.2 Wallpaper modes & rotation (`FR-WP`)
- **FR-WP-1 Default context:** either one **fixed** wallpaper, or **rotation** across any selection of default collections and individual wallpapers, including the user's own images.
- **FR-WP-2 Game context:** each game uses either a fixed wallpaper (the user's favorite) or rotation across the game's enabled wallpapers. The default is rotation across all wallpapers in its pack.
- **FR-WP-3 Rotation interval:** 5 min, 15 min, 30 min (default), 1 h, 3 h, 6 h, daily, "on every unlock/login", or a custom value from 1 min to 7 days. The default context and game contexts each have their own interval, with game contexts defaulting to "no rotation during a session; next wallpaper next session".
- **FR-WP-4 Order:** shuffle (default) or sequential. Shuffle MUST use a shuffle bag, so no wallpaper repeats until all have been shown and the last one never repeats immediately. The position in each context MUST persist across app restarts.
- **FR-WP-5 Context switch behavior:** entering a game context MUST NOT advance the default rotation. When the game ends, the default context resumes with the wallpaper it showed before, unless that wallpaper's interval has elapsed, in which case it advances once.
- **FR-WP-6 Sleep/resume and missed ticks:** after resume or a long suspend, rotate **at most once** no matter how many ticks were missed. Use wall-clock deadlines, not cumulative timers.
- **FR-WP-7 Manual controls:** "Next wallpaper" (tray and settings), "Pause for 1 h", "Pause until I resume". Pausing freezes the current wallpaper. Game detection still runs, but its result isn't applied.
- **FR-WP-8 Light/Dark follow (SHOULD):** an option to restrict default rotation to wallpapers tagged `tone: light` while Windows is in light mode and `tone: dark` in dark mode, read from `HKCU\...\Themes\Personalize\AppsUseLightTheme`, with live updates.
- **FR-WP-9 Battery (SHOULD):** an option, on by default, to pause *rotation* (not game switching) while Battery Saver is on.
- **FR-WP-10 Preview:** "Preview on desktop" applies a wallpaper temporarily for 15 s, or until dismissed, then reverts.

### 3.3 Multi-monitor (`FR-MON`)
- **FR-MON-1** MUST enumerate monitors through `IDesktopWallpaper` (`GetMonitorDevicePathCount`/`GetMonitorDevicePathAt`/`GetMonitorRECT`) and resolve each monitor's **physical pixel size and orientation**. Don't trust DPI-virtualized sizes: the process MUST be Per-Monitor-V2 DPI aware.
- **FR-MON-2 Modes:**
  - *Same wallpaper on all monitors* (default): each monitor still gets its own aspect-correct variant.
  - *Different wallpaper per monitor*: drawn from the same context, with no duplicates when the pool allows it.
  - *Span across monitors* (MAY, v1.1): `DWPOS_SPAN` with a 32x9 variant when two 16:9 monitors sit side by side.
- **FR-MON-3** The game context applies to **all monitors** by default. Option: "Only change secondary monitors during games".
- **FR-MON-4** MUST re-evaluate and re-apply on display changes (hot-plug, resolution or orientation change, docking) through `WM_DISPLAYCHANGE`/`SystemEvents.DisplaySettingsChanged`, debounced by 2 s.

### 3.4 Applying wallpapers (`FR-APPLY`)
- **FR-APPLY-1** Apply through COM `IDesktopWallpaper::SetWallpaper(monitorId, path)` + `SetPosition(DWPOS_FILL)` on a dedicated **STA** thread. Fall back to `SystemParametersInfoW(SPI_SETDESKWALLPAPER, ..., SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)` only if COM fails, which leaves all monitors on the same wallpaper.
- **FR-APPLY-2 Exact-pixel rendering:** for each monitor, choose the variant whose aspect ratio is closest to the monitor's, focal-crop the small remaining difference using the wallpaper's `focal` point, resize with a high-quality filter (Lanczos3, or Mitchell for downscale), and write a **PNG** to the render cache at exactly the monitor's pixel size. Rotated monitors use portrait variants.
  *Why PNG:* Windows re-encodes JPEG wallpapers to about 85% quality, but leaves PNG alone. The app MUST NOT change the user's `JPEGImportQuality` registry value.
- **FR-APPLY-3 Cache:** the file name is `{wallpaperId}_{variant}_{w}x{h}_{contentHash8}.png` under `%LOCALAPPDATA%\PrettyDesk\cache\render\`. Reuse a file when one exists. Every distinct image MUST get a distinct path, because Windows may not refresh when the same path is reused. LRU-evict above 750 MB, and never evict a file currently applied to any monitor.
- **FR-APPLY-4 Idempotence:** skip the apply when the target path for a monitor already equals what is applied there. Never apply more than once per monitor per 2 s; coalesce bursts.
- **FR-APPLY-5 Low impact:** decode, resize and encode on a background thread at `BelowNormal` priority. The apply call itself MUST be the only work done when switching.
- **FR-APPLY-6 Explorer restarts:** listen for the `TaskbarCreated` window message and re-apply the current state, retrying 3 times with backoff if COM calls fail.
- **FR-APPLY-7 Environment conflicts (detect and explain in Home and Settings, never "fix" silently):**
  - Wallpaper policy locked (`HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System\Wallpaper`, `...\Policies\ActiveDesktop\NoChangingWallPaper`, or the HKLM equivalents) → show "Your organization manages your wallpaper" and disable applying.
  - Wallpaper Engine (`wallpaper32.exe`/`wallpaper64.exe`) or Lively Wallpaper running → warn that it draws over the desktop and PrettyDesk changes won't be visible.
  - Windows Spotlight, or the built-in Slideshow as the background type → applying switches the background type to Picture. Tell the user once during onboarding. Restore uses the backup (FR-RESTORE).
  - Windows not activated → the API still works. This is a test-matrix item; no special handling is expected, so verify it.

### 3.5 Original wallpaper backup & restore (`FR-RESTORE`)
- **FR-RESTORE-1** Before the first apply ever, snapshot each monitor's wallpaper path (`IDesktopWallpaper::GetWallpaper`), the position, background color, and background type, then **copy** the files into `%LOCALAPPDATA%\PrettyDesk\backup\`. Copy `%APPDATA%\Microsoft\Windows\Themes\TranscodedWallpaper` as well when the source path is missing.
- **FR-RESTORE-2** "Restore my original wallpaper" in Settings, and automatically on uninstall (Velopack uninstall hook), puts back the snapshot. It MUST work when the original source file has since been deleted, by using the backup copy.
- **FR-RESTORE-3** Quitting from the tray does **not** restore by default. Option: "Restore original wallpaper when PrettyDesk exits".

### 3.6 Content: catalog, packs, downloads (`FR-CON`)
- **FR-CON-1** The app ships with a bundled **catalog snapshot** and a **starter set**: one wallpaper per default collection in the `16x9` variant, plus thumbnails for everything. That's enough to be fully usable offline, with an installer size budget of 60 MB of content or less.
- **FR-CON-2** On startup and every 24 h, fetch `catalog.json` + `catalog.json.sig` from `ContentBaseUrl` (using an ETag). Verify the **ECDSA P-256/SHA-256** signature against the public key embedded in the app. If verification fails, keep the last good catalog and log the failure. The catalog MUST NOT carry anything executable.
- **FR-CON-3** Pack downloads fetch **only the variants needed for the currently attached monitors**, plus thumbnails, with HTTP range resume, SHA-256 verification, write-to-temp, and atomic rename. Run at most 2 concurrent downloads.
- **FR-CON-4 Prefetch:** download the packs for installed and enabled games in the background, so the wallpaper is ready the moment a game starts. If a game starts before its pack is downloaded, keep the current wallpaper and apply as soon as the download completes, provided the game is still active.
- **FR-CON-5 Updates:** when a pack `version` increases, download the new variants and keep the old ones until the swap completes. A wallpaper removed from the catalog that the user has favorited stays usable locally.
- **FR-CON-6 Storage:** `%LOCALAPPDATA%\PrettyDesk\packs\{packId}\v{version}\`. Show the size in Settings with a "Clear downloaded content" button. Default cap: 3 GB, LRU by pack, excluding packs for installed games and default collections that are in use.
- **FR-CON-7 User content:** users can add their own images (JPEG/PNG/WebP/BMP, minimum 1280 px on the long edge) to the default rotation or to any game. The app copies them into `%LOCALAPPDATA%\PrettyDesk\user\`, center-focal-crops them through the same render path, and warns when an image is smaller than the monitor's resolution.

### 3.7 Custom games (`FR-CUSTOM`)
- **FR-CUSTOM-1** "Add a game": choose from currently running processes (a list of apps with visible windows, showing icon and name) or browse to an `.exe`. Set a display name and choose wallpapers from user images and/or any pack.
- **FR-CUSTOM-2** Custom rules are stored in settings and merged with catalog rules. User rules win over catalog rules for the same exe.
- **FR-CUSTOM-3 (SHOULD)** "Suggest this game to PrettyDesk" opens a prefilled GitHub issue URL containing only the exe name and display name, and only after an explicit click.

### 3.8 App shell (`FR-APP`)
- **FR-APP-1** A single instance enforced through a named mutex. A second launch signals the first one to open its window and then exits.
- **FR-APP-2** A tray icon is always present while the app runs. It MUST adapt to the light/dark taskbar. Its menu contains:
  - a status line, such as "Playing: *Game*", "Default · Matte Black (rotating)" or "Paused".
  - Next wallpaper
  - Pause ▸ (1 h / until resumed) or Resume
  - Open PrettyDesk
  - Quit

  Double-click opens the window.
- **FR-APP-3 Start with Windows:** an `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry with a `--background` arg, so the app starts in the tray without a window. On by default; the toggle is in Settings.
- **FR-APP-4** Closing the window hides it to the tray. The first time this happens, show a one-time hint. The window and its view models are **released** on close to keep idle memory low.
- **FR-APP-5 Notifications** are minimal and opt-out per type: pack download failures, unknown-game hints, and update available. No notification on every wallpaper change.

---

## 4. Non-functional requirements (`NFR`)

| ID | Requirement | Target |
|---|---|---|
| NFR-1 | Idle CPU, window closed, measured over 10 min on a 4-core laptop | < 0.2% average, no sustained spikes |
| NFR-2 | Idle memory, window closed | < 80 MB private bytes (WPF baseline-aware), with no growth over a 24 h soak |
| NFR-3 | Game start → wallpaper visible (pack already cached) | ≤ `detectDelay` + 1.5 s |
| NFR-4 | Game exit → default visible | ≤ `exitGrace` + 1.5 s |
| NFR-5 | Cold start to tray icon | < 1.5 s on an SSD |
| NFR-6 | Permissions | Never requires admin. Per-user install. No services, drivers or scheduled tasks. |
| NFR-7 | Offline | Fully functional with the bundled starter set. All network failures are non-fatal and quiet. |
| NFR-8 | Platforms | Windows 11 21H2 (build 22000) and later, **win-x64 and win-arm64** native builds. Windows 10 22H2 is best-effort. |
| NFR-9 | Display | Per-Monitor-V2 DPI aware, correct at 100–300% scaling and with mixed-DPI multi-monitor setups |
| NFR-10 | Accessibility | Full keyboard navigation, visible focus, AutomationProperties names on every control, WCAG AA contrast, respects "Animation effects" off |
| NFR-11 | Localization-ready | Every user-facing string lives in `.resx`. v1 ships `en-US` only. |
| NFR-12 | Privacy | No telemetry by default. The process list never leaves the machine. Only matched game ids are logged. |
| NFR-13 | Reliability | No unhandled exception may crash the tray. Global handlers log the error and keep running, or show a friendly error and restart. |

---

## 5. Architecture

### 5.1 Tech stack (decided; don't swap without an ADR in `docs/adr/`)
| Concern | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# latest, `Nullable` enabled, `TreatWarningsAsErrors` | LTS support window, and published self-contained so users install nothing |
| UI | **WPF** + **WPF-UI** (lepoco/wpfui, MIT) for Fluent/Mica styling | Mature, reliable unpackaged desktop UI with a native Windows 11 look; easier tray and packaging story than WinUI 3 |
| MVVM / DI | CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting/DI/Options | Standard and testable |
| Tray | H.NotifyIcon.Wpf (MIT) | Reliable tray with dark-mode-aware menus |
| Win32 interop | **Microsoft.Windows.CsWin32** source generator | Type-safe P/Invoke and COM (`IDesktopWallpaper`, Toolhelp, WinEvent hooks) |
| Imaging | **SkiaSharp** (MIT, native win-x64 + win-arm64) | High-quality resampling, works cross-platform so image code is unit-testable on Linux CI |
| Logging | Serilog → rolling file, 7 days, 10 MB cap | |
| Installer/updates | **Velopack** | Per-user installs, delta updates, GitHub Releases feed, install/uninstall hooks, x64 + arm64 |
| Tests | xUnit v3, FluentAssertions (or Shouldly), NSubstitute, `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider) | |
| Asset pipeline | Python 3.12, Pillow, numpy, PyYAML, `realesrgan-ncnn-vulkan` binary | Runs on the developer machine; not shipped |

Do **not** enable `PublishTrimmed` (WPF doesn't support it). Enable `PublishReadyToRun` for startup speed. Don't use single-file compression: it slows startup and raises AV-heuristic false positives.

### 5.2 Solution layout
```
PrettyDesk.sln
src/
  PrettyDesk.Core/           net10.0 — pure logic, NO Windows APIs (testable on any OS)
    Catalog/                 models, JSON (System.Text.Json source-gen), signature verification
    Detection/               rule matching, GameSessionTracker (debounce/grace/priority)
    Orchestration/           WallpaperOrchestrator (state machine), context resolution
    Rotation/                RotationScheduler, ShuffleBag, persisted positions
    Imaging/                 VariantSelector, FocalCropper, Renderer (SkiaSharp), RenderCache
    Content/                 PackStore, Downloader (HttpClient), CatalogService
    Settings/                Settings model, versioned JSON store, migrations
    Abstractions/            IProcessSource, IForegroundSource, ISteamRunningAppSource,
                             IMonitorProvider, IWallpaperSetter, ISystemState, IInstalledGameScanner
  PrettyDesk.Windows/        net10.0-windows — implementations of the abstractions via CsWin32
  PrettyDesk.App/            net10.0-windows WPF — views, view models, tray, hosting, startup
tests/
  PrettyDesk.Core.Tests/     runs on Linux + Windows
  PrettyDesk.Windows.Tests/  Windows-only integration tests (skipped elsewhere)
tools/
  assetpipe/                 Python asset pipeline (see §8)
  catalog-sign/              small .NET console: sign/verify catalog.json
art/
  prompts/                   YAML prompt files (see ART_DIRECTION.md)
  raw/                       gitignored; ChatGPT outputs dropped here
  out/                       gitignored; pipeline output
content/                     catalog source (catalog.src.json) — published by CI
docs/
```

### 5.3 Key components
- **`GameSessionTracker`** (Core). Inputs: process snapshots, foreground changes, Steam RunningAppID changes, and the clock. Output: an `ActiveGameChanged(gameId?)` event that applies debounce, grace and priority (FR-DET-4/5/6). Pure and deterministic, so tests drive it with `FakeTimeProvider`.
- **`WallpaperOrchestrator`** (Core). It owns the state machine (§5.4). It combines active game, pause state, settings, monitors, rotation position and content availability into a **desired state**, a map of monitor → render request, then diffs that against the **applied state** and calls `IWallpaperSetter` only for the differences.
- **`RotationScheduler`** (Core). Holds one wall-clock deadline per context, a shuffle bag per context, and persisted positions. Handles resume and missed ticks (FR-WP-6).
- **`Renderer` + `RenderCache`** (Core). Turns `(wallpaper, variant, monitor pixel size)` into a cached PNG path.
- **`DesktopWallpaperSetter`** (Windows). A COM wrapper on a dedicated STA thread with a queue, call timeouts (5 s) and retries.
- **`ProcessSource`** (Windows). A Toolhelp snapshot returning `(pid, exeName, parentPid, startTime?)`. Image path and window title are fetched lazily, only for candidate processes.

### 5.4 Orchestrator state machine
```
            ┌──────── Pause (manual/battery*) ─────────┐
            ▼                                          │
 [Paused] ──Resume──► [Default] ◄────exitGrace done──── [GameGrace]
                        │  ▲                              ▲
   game active (≥3 s)   │  │ game disabled / removed      │ last process gone
                        ▼  │                              │
                     [Game(id)] ────────────────────────────┘
   * Battery Saver pauses rotation only, not context switches.
```
- The desired state is recomputed on any input event, debounced by 250 ms. Applying is diff-based (FR-APPLY-4).
- `Game(id)` with content not yet downloaded → stay on the current wallpaper with the status "Getting *Game* wallpapers…".
- All state-machine transitions MUST be logged at `Information` level with the reason, using game ids only.

### 5.5 Threading
- **UI thread:** WPF dispatcher.
- **STA wallpaper thread:** all `IDesktopWallpaper` calls.
- **WinEvent hook thread:** a dedicated thread with a message loop, because the hook needs one.
- **Everything else:** `async` on the thread pool. Use `Channel<T>` to send events to the orchestrator, which processes them **serially** on a single consumer loop, so it never needs locks.

### 5.6 Data locations
| Path | Content |
|---|---|
| `%LOCALAPPDATA%\PrettyDesk\settings.json` | User settings (schema below), written atomically (temp + replace) |
| `%LOCALAPPDATA%\PrettyDesk\state.json` | Rotation positions, last applied state, backup metadata |
| `%LOCALAPPDATA%\PrettyDesk\catalog\` | Last good catalog + signature |
| `%LOCALAPPDATA%\PrettyDesk\packs\` | Downloaded pack variants |
| `%LOCALAPPDATA%\PrettyDesk\cache\render\` | Per-monitor PNGs |
| `%LOCALAPPDATA%\PrettyDesk\user\` | User-added images |
| `%LOCALAPPDATA%\PrettyDesk\backup\` | Original wallpaper snapshot |
| `%LOCALAPPDATA%\PrettyDesk\logs\` | Serilog files |

Velopack installs the binaries to `%LOCALAPPDATA%\PrettyDesk\current\`. Data folders MUST be siblings that survive updates. Uninstall removes binaries; the uninstall prompt asks before deleting user data.

### 5.7 Settings schema (v1)
```jsonc
{
  "schemaVersion": 1,
  "general": { "startWithWindows": true, "restoreOnExit": false, "paused": false, "pauseUntil": null },
  "detection": { "enabled": true, "pollSeconds": 2, "detectDelaySeconds": 3, "exitGraceSeconds": 10, "unknownGameHints": true },
  "default": {
    "mode": "rotate",                      // "fixed" | "rotate"
    "fixedWallpaperId": null,
    "selection": { "collections": ["default.matte-black"], "wallpapers": [], "excluded": [] },
    "interval": "PT30M",                   // ISO-8601 duration, or "unlock"
    "order": "shuffle",                    // "shuffle" | "sequential"
    "followWindowsTheme": false,
    "pauseRotationOnBatterySaver": true
  },
  "games": {
    "valorant": { "enabled": true, "mode": "rotate", "fixedWallpaperId": null, "excluded": [], "interval": "session" }
  },
  "customGames": [
    { "id": "custom-1a2b", "displayName": "My Indie Game", "exeNames": ["MyGame.exe"], "wallpapers": ["user:abc.png", "valorant.hero-01"] }
  ],
  "monitors": { "mode": "same", "gameOnSecondaryOnly": false },
  "content": { "maxCacheGB": 3, "prefetchInstalledGames": true },
  "notifications": { "downloadErrors": true, "updates": true }
}
```
Unknown fields are preserved on save, to stay forward-compatible. Migrations are pure functions `vN → vN+1` with tests. A corrupt settings file is backed up as `settings.corrupt-<timestamp>.json` and replaced with defaults, and the user is notified once.

---

## 6. Content system

### 6.1 Variants (aspect-ratio families)
| Key | Nominal ratio | Canonical stored size | Covers |
|---|---|---|---|
| `16x9` | 1.778 | 3840×2160 | 1080p, 1440p, 4K, most laptops |
| `16x10` | 1.600 | 3840×2400 | MacBook-style laptops, 1920×1200, 2560×1600 |
| `3x2` | 1.500 | 3000×2000 | Surface devices, some 2-in-1s |
| `21x9` | 2.370 | 5120×2160 | 2560×1080, 3440×1440, 5K2K |
| `32x9` | 3.556 | 5120×1440 | Super-ultrawide (49") |
| `9x16` | 0.5625 | 2160×3840 | Portrait/rotated monitors |

Selection: pick the variant with the smallest `|ln(monitorRatio) − ln(variantRatio)|`, focal-crop to the exact ratio, then resize to exact pixels. A missing variant falls back to the next closest one. `9x16` is optional per wallpaper; when it's absent, focal-crop from `16x9` for portrait monitors and show a quality notice in the debug log.

### 6.2 Catalog format (`catalog.json`, schemaVersion 1)
```jsonc
{
  "schemaVersion": 1,
  "catalogVersion": "2026.10.01.1",
  "minAppVersion": "1.0.0",
  "contentBaseUrl": "https://content.example.com/v1/",
  "games": [
    {
      "id": "cs2", "displayName": "Counter-Strike 2",
      "detection": { "exeNames": ["cs2.exe"], "steamAppIds": [730], "pathContains": [], "windowTitleContains": [], "excludeExeNames": [] },
      "packId": "game.cs2", "addedIn": "2026.10.01.1"
    }
  ],
  "packs": [
    {
      "id": "game.cs2", "kind": "game", "version": 1, "title": "Counter-Strike 2",
      "wallpapers": [
        {
          "id": "cs2.hero-01", "title": "Desert Courtyard at Dawn",
          "tone": "dark", "tags": ["warm", "architectural", "hero"], "setupMatch": ["wood", "black"],
          "focal": { "x": 0.64, "y": 0.46 },
          "accent": "#D9A441",
          "thumb": { "path": "packs/game.cs2/v1/cs2.hero-01_thumb.jpg", "w": 640, "h": 360, "sha256": "…" },
          "variants": {
            "16x9": { "path": "packs/game.cs2/v1/cs2.hero-01_16x9.jpg", "w": 3840, "h": 2160, "bytes": 2512331, "sha256": "…" }
          }
        }
      ]
    }
  ],
  "collections": [
    { "id": "default.matte-black", "packId": "default.matte-black", "title": "Matte Black", "setupMatch": ["black"], "tone": "dark", "order": 1 }
  ],
  "disclaimer": "PrettyDesk is an independent fan project and is not affiliated with or endorsed by any game publisher."
}
```
- Pack variants are **JPEG, quality 95, 4:4:4 chroma, progressive**, sRGB with an embedded profile. (The app converts them to PNG for applying; see FR-APPLY-2.)
- `catalog.src.json` lives in the repo. `tools/assetpipe publish` fills in sizes and hashes, writes `catalog.json`, signs it, and uploads.
- Signing key: the private key lives only in a GitHub Actions secret, or offline. The public key is compiled into `PrettyDesk.Core`. Key rotation: the app accepts a list of public keys.

### 6.3 Hosting
`ContentBaseUrl` is configurable at build time. **Recommended:** a Cloudflare R2 bucket on a custom domain (no egress fees). Acceptable for beta: GitHub Releases on a separate `prettydesk-content` repo. Every URL in the catalog is relative to `contentBaseUrl`, which makes migration trivial.

---

## 7. UI/UX specification

Design language: Windows 11 Fluent (Mica backdrop, rounded corners, Segoe UI Variable, system accent color), following light/dark mode. It should feel calm, premium and minimal, like the wallpapers. The window is 1000×680 default, with a 860×560 minimum, and remembers its size and position.

**Navigation** (horizontal WPF tab strip below the native title bar; see
[ADR 0015](adr/0015-top-tab-navigation.md)): Home · Library · Defaults · Settings · About.
Keep localized accessible names, keyboard tab/arrow navigation, a visible focus
indicator, and a selected-state marker that does not depend on color alone.

1. **Home:** a large live preview of the current desktop composition: each monitor drawn to scale with its current wallpaper thumbnail. Below it is the status card ("Playing *Game* — wallpaper 2 of 4" / "Default · Matte Black · next change in 12 min" / "Paused"), with primary actions *Next wallpaper* and *Pause*. Conflict banners from FR-APPLY-7 appear here.
2. **Library:** a grid of game cards (16:9 thumbnail from the pack's hero art, display name, status chip: *Installed*, *Ready*, *Downloading 42%*, *Not downloaded*). There's a search box, filters (All / Installed / Enabled), and an "Add a game" button. **Game detail page:** an enable toggle, mode (Fixed/Rotate), a wallpaper strip with ☆ favorite, include checkboxes and *Preview on desktop*, rotation options, a detection rules summary (read-only for catalog games, editable for custom games), and "Remove" for custom games.
3. **Defaults:** collection cards (Matte Black, Clean White, …) with multi-select, plus a "My images" collection with drag-and-drop import. Mode Fixed/Rotate, interval, order, and the follow-Windows-theme toggle. A "Setup style" re-run button opens the onboarding quiz.
4. **Settings:** General (startup, restore on exit), Detection (timings, unknown-game hints, pause), Monitors (mode, game-on-secondary-only), Storage (usage, cap, clear), Notifications, Advanced (a live **Detection log** that shows the foreground window's exe name and any catalog match, displayed only and never written to disk, used to verify exe names; open logs folder; export diagnostics zip with logs + settings + monitor info and no process lists, reset app), and Restore original wallpaper.
5. **About:** version, update check, licenses (generated third-party notices), disclaimer, links.

**Quality bar for the UI:**
- Every async action has a loading state.
- Every failure gives a human-readable message and a next step.
- Empty states are designed, not blank.
- Thumbnails load lazily and are decoded at display size (`DecodePixelWidth`).
- There's no layout shift, and the app has zero binding errors in debug output.

---

## 8. Asset pipeline (`tools/assetpipe`)

This is a developer-only Python CLI that turns the owner's ChatGPT image outputs into publishable variants.

```
assetpipe status                    # table of every wallpaper in art/prompts/*.yaml: which raw files exist, which are missing
assetpipe validate [--pack ID]      # raw files present, decodable, min size, aspect sanity, sRGB
assetpipe build --pack ID           # upscale → crop → grade → dither → encode → art/out/
assetpipe review --pack ID          # HTML contact sheet: every variant with icon/taskbar safe-zone overlays + 100% crops
assetpipe publish --pack ID         # update catalog.src.json → catalog.json, sign, upload (R2 via rclone/aws-cli)
assetpipe prompts                   # regenerate art/PROMPTS.md (copy-paste friendly) from YAML
```

**Raw input naming:** `art/raw/{packId}/{wallpaperId}_{L|U|P}.png`. `L` is the landscape master (16:9), `U` is the ultrawide master (3:1 target and 21:9 minimum, made by extending L in ChatGPT), and `P` is the portrait master (9:16, optional). See ART_DIRECTION.md §6.

**Source sizes:** as of late 2026, ChatGPT image generation (GPT Image 2) outputs about 2K natively, with 4K in beta and aspect ratios up to 3:1. The pipeline MUST accept any size of 1536 px or more on the long edge, and MUST NOT assume a fixed resolution. Most masters will need upscaling to reach the canonical sizes.

**Build steps for each wallpaper:**
1. Load as 16-bit-per-channel RGB and convert to sRGB if it's tagged otherwise.
2. **Upscale** when the source is smaller than the target. Use `realesrgan-ncnn-vulkan` with model `realesrgan-x4plus` for painterly or photographic art, or `realesrgan-x4plus-anime` for flat or illustrated styles. The per-wallpaper `upscaler` field chooses the model. Upscale 4× and then downscale to the target, never stretch. Skip the upscale when the source is already large enough.
3. Derive the variants:
   - `L` → `16x9`, `16x10`, `3x2`
   - `U` → `21x9`, `32x9`
   - `P` → `9x16`

   Each one is a focal-point crop using `focal` from the YAML, followed by a Lanczos resize to the canonical size.
4. **Anti-banding:** add triangular-PDF dither when quantizing to 8-bit, plus optional film grain set by the `grain` field (0–1, default 0.15; use about 0.3 for gradient-heavy minimal art).
5. Encode as JPEG q95, 4:4:4, progressive, with an embedded sRGB ICC profile. Also write a 640×360 thumbnail (q85).
6. Checks that fail the build: an output dimension that doesn't match the target, a file over 8 MB, or a banding heuristic over threshold (count unique levels in the smoothest 10% of 64×64 tiles; flag "possible banding" when they show visible steps).

**Review contact sheet:** for each variant, overlay the desktop-icon column (the left 12% of width), the taskbar (bottom 48 px at 1080p scale) and the focal point. The owner approves each wallpaper by setting `approved: true` in its YAML. `publish` refuses unapproved wallpapers.

Raw and out folders are **gitignored**. The owner keeps raw files in cloud storage. The YAML stores `rawSha256` so outputs stay traceable.

---

## 9. Packaging, distribution & updates

- **Builds:** `dotnet publish -c Release -r win-x64 --self-contained` and the same for `win-arm64`, with `PublishReadyToRun=true` and no trimming.
- **Velopack:** run `vpk pack` for each RID. Outputs are `PrettyDesk-win-x64-Setup.exe`, `-Portable.zip`, and full plus delta packages. The update feed is GitHub Releases on the app repo, with channels `stable` and `beta`. The app checks for updates on startup and every 12 h, downloads in the background, and applies on next restart, or right away when the user clicks "Restart to update".
- **Velopack hooks:**
  - install: create the Start menu shortcut and enable "start with Windows"
  - uninstall: restore the original wallpaper, remove the Run key, and ask whether to keep user data
  - first run: open onboarding
- **Code signing is required for public release.** Unsigned EXEs trigger SmartScreen ("Windows protected your PC") and AV false positives. Use **Azure Trusted Signing** (cheapest; check eligibility for individuals in the owner's region) or an OV certificate. Sign the app binaries, `Setup.exe` and update packages through Velopack's signing options in CI. Note that SmartScreen reputation builds over the first few hundred downloads even when the files are signed.
- **AV hygiene:** don't pack or compress the binaries, use clear assembly metadata (company, product, description), and don't run hidden PowerShell. If Defender flags a build, submit it at the Microsoft Security Intelligence portal.
- **Later channels (MAY):** a winget manifest after 1.0, and a Microsoft Store listing (MSIX) later.
- **Versioning:** SemVer, with the version derived from the git tag (`v1.2.3`) through MinVer or Nerdbank.GitVersioning.

### 9.1 CI (GitHub Actions)
- `ci.yml`, on PR and push, runs on `windows-latest`:
  1. restore
  2. `dotnet format --verify-no-changes`
  3. build with warnings as errors for both RIDs
  4. Core + Windows tests
  5. upload test results

  An `ubuntu-latest` job runs the Core tests too, to keep Core portable.
- `release.yml`, on tag `v*`: publish both RIDs → sign → `vpk pack` → upload a GitHub Release (draft) with generated release notes.
- `content.yml` (manual dispatch): validate `catalog.src.json` against the JSON schema → sign → upload to the content host.

---

## 10. Privacy, security & legal

- **Telemetry:** none. Crash reporting MAY be added later as **opt-in** only.
- **Network calls:** only the catalog, packs and the update feed. Every URL is HTTPS. The User-Agent is `PrettyDesk/<version>` and carries no identifiers.
- **Logs:** never log full process lists, file paths under the user profile beyond `%LOCALAPPDATA%\PrettyDesk`, or the user name.
- **Supply chain:** pin NuGet versions with a lock file (`RestorePackagesWithLockFile`), enable Dependabot, and generate `THIRD-PARTY-NOTICES.txt` at build time.
- **IP / trademark (important):**
  - Wallpapers are **original art inspired by** each game's mood, palette and genre. They MUST NOT contain game logos, title text, official character likenesses, or recreations of official key art (see ART_DIRECTION.md §5).
  - Using game names to label detection targets is descriptive. Show the disclaimer in About and in the installer.
  - Never use official logos or icons in the UI; game cards use our own art.
  - If the app is ever monetized, have a lawyer review the game-themed packs first.
- **Third-party licenses:** WPF-UI (MIT), H.NotifyIcon (MIT), SkiaSharp (MIT), Velopack (MIT), Serilog (Apache-2.0), Real-ESRGAN (BSD-3; tooling only, not shipped). Verify each one at implementation time.

---

## 11. Testing & QA

### 11.1 Automated (MUST)
- **Core unit tests** (target ≥ 85% line coverage of Core):
  - Rule matching: exe-name case-insensitivity, secondary rules, excludes, custom rules overriding catalog rules.
  - `GameSessionTracker`: debounce, grace, crash-on-launch, self-restart within grace, two games running with foreground priority, a Steam AppID flip.
  - Orchestrator: every transition in §5.4, diff-based applying, pause, content-not-ready, a monitor added or removed mid-game.
  - Rotation: the shuffle bag never repeats before exhaustion, no immediate repeats across bag refills, persistence round-trip, a suspend of 3 days rotating exactly once, "unlock" interval.
  - Variant selection for a monitor table covering 1366×768, 1920×1080, 1920×1200, 2256×1504, 2560×1080, 2560×1440, 3440×1440, 3840×2160, 5120×1440, 5120×2160, 1080×1920 and 2160×3840.
  - Renderer: output pixel size is exact, the focal point is respected (synthetic test image with a marker), PNG output.
  - Catalog: signature valid/invalid/tampered, schema validation, unknown fields ignored, `minAppVersion` gating.
  - Downloader: hash mismatch → retry → fail, resume, atomic rename (fake HttpMessageHandler).
  - Settings: migrations, corrupt-file recovery, unknown-field preservation.
- **Windows integration tests:** set and get a wallpaper through COM on a real desktop (CI runner), Toolhelp enumeration returns the current process, Run-key toggle round-trip.

### 11.2 Manual QA matrix (run before every release; checklist in `docs/QA_CHECKLIST.md`, which the implementer writes)
- OS: Windows 11 23H2, 24H2 and 25H2 (or the latest release); one ARM64 device (Snapdragon) if one is available; one Windows 10 22H2 smoke test.
- Displays: one 1080p monitor; laptop at 150% scaling; 4K at 200%; dual monitors with mixed DPI; ultrawide 3440×1440; one portrait-rotated monitor; hot-plug a monitor mid-game; dock/undock.
- States: sleep/resume, lock/unlock, fast user switching, Explorer restart (`taskkill /f /im explorer.exe`), RDP session, Battery Saver, light/dark switch, unactivated Windows, Spotlight background active before install, Wallpaper Engine running.
- Games: at least 5 real games across Steam, Epic, Riot and Battle.net, including one with kernel anti-cheat (Valorant/Vanguard, or EAC/BattlEye titles). Confirm there are no anti-cheat warnings and no FPS impact (compare PresentMon or in-game FPS with PrettyDesk on and off).
- Install, update (old → new through Velopack) and uninstall: the original wallpaper is restored.

---

## 12. Milestones & acceptance criteria

Implement these in order. Each milestone ends with green CI and a short demo note in the PR description.

| # | Milestone | Done when |
|---|---|---|
| M0 | Repo scaffold | Solution/projects per §5.2, Directory.Build.props (nullable, warnings-as-errors, analyzers, `LangVersion latest`), `.editorconfig`, `.gitignore`, `ci.yml` green, README |
| M1 | Core logic | Detection matching, GameSessionTracker, RotationScheduler, Orchestrator, settings store, all with the unit tests from §11.1. No UI. |
| M2 | Windows layer | CsWin32 interop: ProcessSource, foreground hook, Steam AppID watcher, MonitorProvider, DesktopWallpaperSetter (STA), backup/restore, conflict detection. Integration tests. |
| M3 | Imaging & content | Variant selection, Renderer + RenderCache, catalog load/verify, PackStore, Downloader, bundled starter set, `tools/catalog-sign` |
| M4 | App shell | Hosting, single instance, tray, start-with-Windows, onboarding, the Home/Library/Defaults/Settings/About pages per §7 |
| M5 | Asset pipeline | `tools/assetpipe` with all commands, a contact sheet with overlays, tests for the crop/variant math |
| M6 | Packaging | Velopack, both RIDs, `release.yml`, signing wired (secrets stubbed), update flow verified end to end with a test feed |
| M7 | Hardening | 24 h soak (memory/CPU), QA checklist executed and documented, every NFR measured and recorded in `docs/PERF.md` |

**Art track (parallel; see ART_DIRECTION.md §8):** A1, the default-collection prompts, MUST be finished before M3 so a starter set exists. A2, the game prompts for every seed game, comes next.

---

## 13. Open questions for the owner
(The implementer should pick the noted default and leave a `// OWNER-DECISION:` comment wherever an answer would change code.)
1. Final product name and icon. Default: "PrettyDesk" with a placeholder icon.
2. Content host. Default: GitHub Releases for beta, Cloudflare R2 for 1.0.
3. Code-signing route and budget. Default: Azure Trusted Signing.
4. Free forever vs. monetized later. Default: free; this affects the IP review in §10.
5. Telemetry, ever? Default: none.
