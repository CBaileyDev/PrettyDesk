# 0016: Dashboard visual language replaces the glass look

Date: 2026-10-09. Supersedes the window material and surface decisions of ADRs 0007,
0010, 0011, 0012, 0013 and 0014, at the owner's request ("less like Apple, more like a
premium modern dashboard").

## Decision

The app is drawn with opaque surfaces and no backdrop effect:

- **Layout.** A flush 220 px sidebar and a content area on a canvas colour. No floating
  rounded panels, no gutters of window material around them. The title bar uses the
  sidebar colour so the two read as one frame.
- **Surfaces.** Cards are solid, 1 px bordered, 10 px radius. Buttons and segmented
  controls use 6-8 px radii. There is no acrylic, Mica, transparency or gradient
  background and no shadow, so nothing costs a blur or composition pass (NFR performance).
- **Colour.** One indigo accent (`#5B5BD6`; text-weight variant per theme) replaces system
  blue. Palettes are data: `Resources/Palette.Light.xaml`, `Palette.Dark.xaml` and
  `Palette.HighContrast.xaml` (system colours). `AppAppearance` only picks and merges the
  right one, sets the WPF-UI accent, and reacts to theme or high-contrast changes.
- **Controls.** `PrimaryButtonStyle` for the one main action per view, a neutral default
  button, `SegmentedBarStyle`/`SegmentedOptionStyle` for two- and three-way choices,
  `ChoiceCardStyle` for onboarding choices, and `SettingRowStyle` (title and help on the
  left, control on the right) for every toggle and dropdown on Settings, Defaults and the
  game page. Keyboard focus is a visible 2 px ring on every custom control.
- **Home.** The "Liquid Glass" promotional banner and its two bundled images are removed.
  Status and actions come first; pause controls that cannot apply in the current state are
  hidden instead of disabled.

## Consequences

- `DesktopEffectsPreferences` (the transparency-effects probe) and the deferred
  acrylic-application logic in `AppAppearance` are deleted, along with the smoke test that
  asserted acrylic. A new smoke test checks that each palette defines every colour key.
- Colours can be changed in three small XAML files without touching code.
- The Liquid Glass wallpaper collection itself is content and is unchanged.
- The old ADRs remain as history. Where they conflict with this one, this one wins.
- Visual fidelity could not be checked on Linux. `docs/QA_CHECKLIST.md` gains a pass over
  each page in light, dark and high contrast.
