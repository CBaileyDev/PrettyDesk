# 0009: Display-matched onboarding downloads

Date: 2026-10-01. Accepted for the owner's onboarding request.

Retain FR-CON-3's per-monitor aspect selection and FR-APPLY-2's exact-pixel rendering.
Onboarding adds a wallpaper step between installed games and startup. Show the
detected display setup, selected installed-game packs, missing download size, local
readiness, unavailable hosting, progress and explicit retry. Keep download actions
and background choice visible while the pack list scrolls. Setup can continue
without waiting for the network.

Use the actual content library's missing-file plan for both estimates and acquisition.
Two 2560×1440 landscape monitors request one 16:9 variant per wallpaper, plus its
thumbnail. They do not imply spanning or two image copies. Mixed displays request
the union of available aspect families; absent portrait artwork uses the baseline
16:9 crop fallback with a visible notice. Resolution does not select a second
download: render the provided master to the monitor's exact pixel dimensions.
Account for bundled current-hash copies and downloaded files, and request additional
formats when a display change occurs during an existing download.

Separate enabling a game's wallpaper switching from prefetching its pack. Persist
`GameSettings.PrefetchWallpapers` (default true for existing settings) and the
existing global prefetch preference. Expose the per-game preference in Library
details so it remains editable after setup. Unchecking stops future prefetch, not
already started transfers; show this clearly. Manual downloads remain available
with automatic downloads disabled. Keep selected preferences through a transient
display disconnect. Missing hosting or missing artwork never becomes a fake
successful download. Scan failure is retryable and distinct from no installed games.

Validation uses deterministic content/monitor fixtures and isolated WPF renders.
Real installation discovery, production-host downloads, display hot-plug and the
full native lifecycle matrix remain separate acceptance checks.
