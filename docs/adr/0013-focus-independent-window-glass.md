# 0013: Clearer glass independent of focus

Date: 2026-10-02. Owner requested clearer main-window glass and persistence when
switching to another app after reviewing beta.9.

On Windows 11 build 26100 or newer, enable documented DWMWA_REDIRECTIONBITMAP_ALPHA
on our own HWND and keep WPF's composition background transparent. Disable the
transient system backdrop on this path: its inactive material policy is the source
of the appearance change. Lower the outer, navigation and main-panel tints while
retaining dense readable cards and opaque foreground colors. This is alpha glass,
not the system acrylic blur. No focus spoofing, foreign-window hooks or new dependency.

High contrast and disabled Windows transparency retain opaque surfaces. Unsupported
or failed native alpha enablement uses the existing system acrylic fallback; its
inactive-window behavior remains controlled by Windows. Actual desktop pixel blending
requires live acceptance. Focus tests verify real deactivation, enabled alpha and
unchanged palette, not screenshots of the DWM compositor.

Reference: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute

## Superseded after visual review

The owner rejected beta.10 because it removed the intended glass effect.
[ADR 0014](0014-restore-genuine-acrylic.md) restores real acrylic blur in beta.11.
The alpha-only path and helper were removed.
