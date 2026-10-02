# 0011: Visible glass, offline car artwork and responsive navigation

Date: 2026-10-01. Accepted for the owner's explicit repair request.

The owner requested new Rocket League wallpapers, translucent app/taskbar glass,
and responsive navigation with smooth transitions. Earlier planning and fallback
screenshots did not establish that these outcomes worked on the owner's desktop.

## Material and Windows preference

Use WPF-UI 4.3.0 Desktop Acrylic for our own window. Reduce the opaque tint layered
over it, retain readable cards, and keep opaque high-contrast/transparency-off
fallbacks. Coalesce palette refreshes and skip unchanged palettes. Defer material
application until after initial rendering: changing FluentWindow's WindowChrome
inside Loaded caused a real Freezable inheritance-context crash with transparency
enabled. A queued, coalesced apply avoids that re-entrancy.

Windows Transparency effects was Off. The owner explicitly requested glass; it was
turned On through Windows Colors during this repair, with the original value saved
locally in `TestResults/glass-repair/transparency-before.json`. This is a user-selected
Windows preference, not an app-owned shell modification. To return to the recorded
original appearance, turn Transparency effects Off in Windows Colors. PrettyDesk
does not keep rewriting it or change the global accent. Native taskbar translucency
is controlled by Windows; a custom clear/tinted Explorer taskbar remains separate.
Do not introduce injection, Explorer hooks or a silently installed companion.

Primary references: [DWM system backdrops](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type),
[WPF-UI backdrop implementation](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Controls/Window/WindowBackdrop.cs).

## Navigation and images

Retain the five lazily created page visuals only for the lifetime of the main window.
Collapsed pages cannot receive keyboard focus. This preserves search/scroll state
and avoids rebuilding every template during navigation. Release all pages and the
thumbnail cache on window close, preserving FR-APP-4. Use the existing WPF-UI
VirtualizingGridView for Library cards; no new dependency. Verify scrolling to the
last item and returning to the same scroll position.

Use 160 ms opacity/translation transitions, with no layout animation or continuous
background animation. High contrast and Windows Animation effects Off disable them;
hidden/unloaded hosts clear animation clocks. Thumbnail decoding is bounded by a
16 MiB cache, keyed by file identity/size/time and decode width. Release file handles
immediately and invalidate replaced images. This does not claim asynchronous first
decoding or a constant frame-rate guarantee.

## Offline artwork

Four newly generated car scenes use new IDs: Octane Aerial, Quiet Fennec, Neo Tokyo
Rain, and Beach Arena. Preserve the old generic art and archived pilot masters.
Use the existing Real-ESRGAN x4plus pipeline before canonical landscape renditions;
record actual master hashes. Bundle the approved 16:9 images under the existing
128 MiB policy (FR-CON-1 deviation recorded in ADR 0008). Put the new scenes first
in the pack so its Library cover also changes. Dedicated ultrawide/portrait masters
are not claimed. Public/monetized distribution rights remain a separate release gate.

The hash updater must handle both inline and block YAML maps. Replacing only the
header of a block map corrupts the source; a regression test covers this failure.

Current validation belongs in the repair evidence, not earlier dated acceptance
records. Layout timing measures UI work, not DWM frame presentation or game FPS.
