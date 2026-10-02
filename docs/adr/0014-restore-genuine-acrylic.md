# 0014: Restore genuine frosted acrylic

Date: 2026-10-02. Supersedes ADR 0013 following owner visual rejection of beta.10.

The owner reported that beta.10 had no glass effect. Per-pixel alpha without blur
did not meet the requested design, despite successful native enablement and builds.
Restore the real WPF-UI Desktop Acrylic backdrop and remove the alpha-only renderer.
Choose intermediate outer/panel tint strengths between beta.9 and beta.10 to make
the main surface slightly clearer while retaining dense readable cards.

Preserve opaque high-contrast and transparency-off fallbacks and deferred material
application. Windows still controls inactive system acrylic appearance. Do not
claim persistent unfocused blur, fake focus, or replace blur with transparency.
Windows App SDK offers a configurable acrylic controller, but incorporating its
composition target/runtime requires separately validated integration. No unsupported
native composition API or rendering overlay is introduced for this candidate.

Validation checks native acrylic application, WPF page/binding behavior and actual
focus transitions when foreground activation is permitted. Offscreen captures cannot
establish DWM blur; the owner must visually accept this candidate.

References:
- https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Controls/Window/WindowBackdrop.cs
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.systembackdrops.systembackdropconfiguration.isinputactive
