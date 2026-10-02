# 0008: Personalization implementation choices

Date: 2026-10-01. Accepted for the owner-requested implementation; native acceptance remains separate.

Choose hybrid content delivery: preserve the signed optional feed and bundle the approved 16:9 wallpapers for seven selected game packs. `content/offline-bundle.json` is the authored policy with a 128 MiB content ceiling. This deliberately supersedes the original 60 MiB FR-CON-1 content budget for this update; existing default collections remain required. Never advertise ungenerated art or substitute Forza Horizon 5 for Forza Horizon 6. Preflight source hashes and the complete budget before copying; preserve unrelated bundle files.

Detection retains the baseline snapshot cadence. Reuse name-only matching during an unchanged active session; invalidate on snapshot identity, foreground, Steam, matcher changes and a 30-second fallback. Path/title candidates always re-query. This reduces repeated matching without trading away missed-start latency. It does not claim lightweight native liveness or lower game frame times.

App theme is System by default with persisted Light/Dark overrides; high contrast wins. Neutral opaque surfaces keep text readable. Taskbar support opens Windows Colors only on an explicit click. No taskbar registry writes, companions or foreign-window hooks are introduced.

Desktop surfaces are off by default. The first implementation is a non-topmost, non-activating, click-through window owned by PrettyDesk, positioned in physical monitor coordinates. It shows clock/date, transient Windows now-playing metadata and an optional 15 FPS playback visualizer. Playback controls stay keyboard accessible in Settings. It stops its render timer and WASAPI capture during active games, lock and battery saver. High contrast and reduced motion disable the animated spectrum. Disable/exit closes the surface and releases capture; no shell setting needs restoration. This is an ordinary window prototype, not guaranteed Explorer desktop attachment or a shell-layer contract. Explorer restart, DPI, focus and device-change acceptance remain required.

Use NAudio.Wasapi 2.2.1 (MIT) for documented WASAPI loopback lifecycle instead of handwritten audio COM. Capture scope is the default multimedia output only; no microphone, file recording or upload. Refresh its default-device selection periodically and expose failure text. Refresh normal, RID and ReadyToRun lock graphs and shipped notices together. A broader output selector is deferred.

Named fan art is a per-pack mode. Only the reviewed Rocket League subject allowlist is enabled. Keep generic lint checks, safe zones, approval and source hashes. Preserve approved generic art while new pilot IDs remain unapproved until actual image/crop review. Public rights review remains a separate release gate.
