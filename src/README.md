# Source map

The four projects are already organized by responsibility. Start with
[architecture](../docs/ARCHITECTURE.md) for dependency direction and
[AGENTS.md](../AGENTS.md) for constraints. Use this map to find the implementation.

| Concern | Primary files / folders |
|---|---|
| Entry, single instance, application lifetime | App/[Program.cs](PrettyDesk.App/Program.cs), [SingleInstance.cs](PrettyDesk.App/SingleInstance.cs), [App.xaml.cs](PrettyDesk.App/App.xaml.cs) |
| Wiring and runtime activation | App/Composition/[ServiceRegistration.cs](PrettyDesk.App/Composition/ServiceRegistration.cs), [AppRuntime.cs](PrettyDesk.App/Composition/AppRuntime.cs) |
| Running games, cadence, session handoff | Core/Detection/[DetectionService.cs](PrettyDesk.Core/Detection/DetectionService.cs), [RuleMatcher.cs](PrettyDesk.Core/Detection/RuleMatcher.cs), [GameSessionTracker.cs](PrettyDesk.Core/Detection/GameSessionTracker.cs), [DetectionConfigurator.cs](PrettyDesk.Core/Detection/DetectionConfigurator.cs) |
| Native detection signals / limited process queries | Windows/[ProcessSource.cs](PrettyDesk.Windows/ProcessSource.cs), [ProcessDetailsSource.cs](PrettyDesk.Windows/ProcessDetailsSource.cs), [ForegroundWatcher.cs](PrettyDesk.Windows/ForegroundWatcher.cs), [SteamRunningAppWatcher.cs](PrettyDesk.Windows/SteamRunningAppWatcher.cs) |
| Installed-game discovery / unknown hints | Core/Detection/[Discovery](PrettyDesk.Core/Detection/Discovery), [UnknownGameMonitor.cs](PrettyDesk.Core/Detection/UnknownGameMonitor.cs); Presentation/Services/[InstalledGamesProvider.cs](PrettyDesk.Presentation/Services/InstalledGamesProvider.cs) |
| Selection / preview / rotation | Core/[Orchestration](PrettyDesk.Core/Orchestration), [Rotation](PrettyDesk.Core/Rotation) |
| Crop, variants, render cache | Core/[Imaging](PrettyDesk.Core/Imaging) |
| Wallpaper apply / backup / restore | Windows/[DesktopWallpaperService.cs](PrettyDesk.Windows/DesktopWallpaperService.cs), [WallpaperBackupService.cs](PrettyDesk.Windows/WallpaperBackupService.cs) |
| Signed catalogs, download and user images | Core/[Catalog](PrettyDesk.Core/Catalog), [Content](PrettyDesk.Core/Content) |
| Persistence and privacy | Core/[Settings](PrettyDesk.Core/Settings), [Privacy](PrettyDesk.Core/Privacy); App/[AppPaths.cs](PrettyDesk.App/AppPaths.cs), [Logging.cs](PrettyDesk.App/Logging.cs) |
| Home monitor preview and status | Presentation/ViewModels/[HomeViewModel.cs](PrettyDesk.Presentation/ViewModels/HomeViewModel.cs); App/Views/[HomePage.xaml](PrettyDesk.App/Views/HomePage.xaml) |
| Library and game wallpaper actions | Presentation/ViewModels/[LibraryViewModel.cs](PrettyDesk.Presentation/ViewModels/LibraryViewModel.cs), [GameDetailViewModel.cs](PrettyDesk.Presentation/ViewModels/GameDetailViewModel.cs); App/Views/[GameDetailView.xaml](PrettyDesk.App/Views/GameDetailView.xaml) |
| Shell / pages / appearance | Presentation/[ViewModels](PrettyDesk.Presentation/ViewModels); App/[Views](PrettyDesk.App/Views), [Styles.xaml](PrettyDesk.App/Resources/Styles.xaml), [AppAppearance.cs](PrettyDesk.App/Services/AppAppearance.cs) |
| Tray, window management and native dialogs | App/Services/[TrayService.cs](PrettyDesk.App/Services/TrayService.cs), [WindowManager.cs](PrettyDesk.App/Services/WindowManager.cs), [UiServices.cs](PrettyDesk.App/Services/UiServices.cs) |
| Updates and installation hooks | Presentation/Services/[UpdateCoordinator.cs](PrettyDesk.Presentation/Services/UpdateCoordinator.cs); App/Services/[VelopackUpdateBackend.cs](PrettyDesk.App/Services/VelopackUpdateBackend.cs), [InstallHooks.cs](PrettyDesk.App/Services/InstallHooks.cs) |

`Windows/NativeMethods.txt` and `.json` declare CsWin32 generation inputs. Interop
output in `obj` is generated. `Presentation/Resources/Strings.Designer.cs` is generated
from `Strings.resx` using `tools/strings.py gen`.

Tests mirror these responsibilities under [tests](../tests/README.md). For a new
feature, add portable policy to Core/Presentation, a native adapter to Windows when
needed, and connect it in App composition. Do not put speculative taskbar or widget
code into game detection.
