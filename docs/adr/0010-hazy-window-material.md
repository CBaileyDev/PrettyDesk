# 0010: Hazy application shell and readable cards

Date: 2026-10-01. Accepted for the owner's requested window refinement.

Remove only the redundant visible title-bar wordmark. Retain the window title,
accessible name, draggable title bar, native window commands and centered sidebar
brand. Increase the Home hero/action padding and the Library/collection artwork
height, while retaining scrolling at minimum window size.

Use the existing WPF-UI Acrylic backdrop for our main application window on Windows
11 build 22621+ when Windows advanced effects are enabled and high contrast is off.
Check the native apply result and restore an opaque window background on failure.
Do not set whole-window opacity or `AllowsTransparency`; text and controls remain
fully opaque. Native integration is provided by the existing WPF-UI dependency,
with no game/window hooks or taskbar changes.

Put a lightly tinted static gradient over the native material, a translucent shell
and denser content cards. There are no animated background layers or repeated
wallpaper rendering. When Windows transparency is disabled, use opaque soft-tint
gradients and opaque cards. High contrast uses opaque system brushes. Read the OS
preference through `DesktopEffectsPreferences`, refresh on preference events and
unsubscribe at application exit. No Windows setting is written.

Primary API references:
- [WPF-UI 4.3.0 backdrop implementation](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Controls/Window/WindowBackdrop.cs).
- [Microsoft composition tailoring](https://learn.microsoft.com/en-us/windows/uwp/composition/composition-tailoring).

The current machine has Windows transparency disabled. Inspected WPF captures
therefore validate the opaque fallback and layout; they do not prove live DWM blur,
wallpaper-dependent contrast, GPU cost, mixed DPI or the high-contrast matrix.
Do not enable a machine-wide setting merely to obtain a screenshot.
