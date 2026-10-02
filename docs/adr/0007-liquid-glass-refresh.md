# 0007: Liquid Glass visual refresh and wallpaper capsule

Status: accepted for the user's 2026-10-01 request to make the app Apple/iOS-inspired and add iOS wallpapers.

Keep WPF, Windows window controls, native dialogs, existing commands, and the tray lifecycle. Use original optical artwork,
Segoe UI Variable, floating surfaces and capsule controls. Interpret the requested visual language without shipping Apple assets.
Continue following system light/dark themes, with system brushes for contrast themes. Avoid continuously animated glass effects.

Add one supplemental three-wallpaper collection rather than change the sizes of the original 13 collections. The asset linter
records this new collection's size explicitly. Its `bundled` tag ships every screen ratio offline within the existing 60 MiB budget.
Label the existing Defaults navigation destination Wallpapers to make its content easier to discover; PageKind and settings stay compatible.

Actual WPF renders and verification limits are recorded in IOS_DESIGN.md. This visual refresh does not close the separate
content-host, production-signing, incomplete generation, soak or hardware acceptance gaps.
