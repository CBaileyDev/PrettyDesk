# 0015: Top tab navigation

Date: 2026-10-02. Accepted for the owner's request to make the main window cleaner
and move its section navigation from the left rail to a top tab bar.

This supersedes the centered sidebar-brand and navigation-placement clauses in
[ADR 0010](0010-hazy-window-material.md). Its acrylic, native window-chrome and
opaque-fallback decisions remain in force.

## Decision

Place the existing Home, Library, Wallpapers, Settings and About destinations in a
horizontal WPF `TabControl` beneath the native title bar. Keep a single page surface
below it. The tab strip uses explicit accessible names, visible keyboard focus and a
selection marker in addition to its theme-aware fill. In high contrast, selected and
focus colors come from the active Windows contrast palette.

The compact brand lockup occupies the left side of the same top-shell row.

Keep `ShellViewModel` as the navigation owner and retain its lazy page factories,
`MotionContentControl` cache, short opacity/translation feedback and close-time
release. This returns the current 214-DIP rail width to the page canvas without
adding a dependency, continuous animation or a second page host. The 860-DIP minimum
window remains the layout floor.

## Consequences

- Wallpaper and game grids gain horizontal room; navigation now occupies a short row.
- The destinations expose tab semantics and standard left/right keyboard navigation.
- A non-color selection marker, explicit focus outline and system contrast brushes
  make state easier to distinguish.
- Native title-bar controls, system theme, high contrast, tray lifetime and page state
  remain under their existing owners.

## Validation record

Implementation and current validation results are recorded in
[`2026-10-02-top-navigation-and-default-art.md`](../updates/2026-10-02-top-navigation-and-default-art.md).
