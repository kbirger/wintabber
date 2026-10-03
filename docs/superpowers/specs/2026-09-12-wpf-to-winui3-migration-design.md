# WPF to WinUI 3 Migration — Design

## Goal

Replace `iNKORE.UI.WPF.Modern` with native WinUI 3 controls, and replace the
homegrown `WinTabber.UI.Common/Chrome` acrylic/blur implementation with native
`SystemBackdrop`. Two drivers: modernize the look and feel, and remove
dependency risk carried by a small third-party control package.

This is not driven by a need for WinUI-3-only capability. Nothing in this
document should add a feature the WPF app lacks today — it is a like-for-like
rebuild on a new UI framework.

## Constraint that shapes everything else

WPF (`System.Windows.Application`) and WinUI 3
(`Microsoft.UI.Xaml.Application`) cannot run in the same process. WinTabber
has six windows (`SettingsWindow`, `WindowSelectorWindow`, `ThumbnailWindow`,
`DockWindow`, `SuspendedWindowsWindow`, `MediaControlsWindow`) sharing one app
process today, so there is no way to migrate them one at a time inside the
existing `WinTabberUI` project.

**Decision:** build a second, independent app in a new `winui3/` folder,
side by side with the existing WPF app. The WPF app is not touched (beyond
the Phase 0 cleanup below) and keeps working throughout. The new app is
developed to feature parity, then a separate future decision (out of scope
here) retires the WPF app.

## Two facts that were verified before committing to this design

- **DWM thumbnail compositing inside a WinUI 3 window works.** This was the
  single fact that could have made the whole migration infeasible —
  `ThumbnailWindow.xaml` already has a comment recording a past DWM/WPF
  layering fight, and WinUI 3 content lives in a composition island over the
  host HWND, which could plausibly break `DwmRegisterThumbnail`. Confirmed
  working; not re-verified in this document, but called out because it is
  load-bearing.
- **`ReactiveUI.WinUI` exists as a maintained package**
  (https://www.nuget.org/packages/ReactiveUI.WinUI/), providing
  `ReactivePage`/`ReactiveWindow`/`ReactiveUserControl`-equivalent view
  hosting for WinUI 3. Every view file in the app uses these base classes, so
  this was a second potential blocker; it is not one.

## Phase 0 — WPF-only ViewModel cleanup (no WinUI3 project exists yet)

Done entirely inside today's `WinTabberUI` and `WinTabber.Infrastructure`,
verified by the existing test suite, with no visible behavior change to the
running app. This is valuable independent of the migration (a UI-package
dependency and a WPF-type dependency do not belong in the ViewModel layer),
and it is required before ViewModels can be shared between the WPF and WinUI3
apps.

1. **Icon key abstraction.** Add an `IconKey` enum to `WinTabber.Infrastructure`
   with no reference to any UI package. Replace
   `iNKORE.UI.WPF.Modern.Common.IconKeys` in
   `WinTabber.Infrastructure/Settings/ShortcutCommandCatalog.cs` and in the
   four Settings ViewModels (`AppearanceSettingsViewModel`,
   `GeneralSettingsViewModel`, `SettingsViewModelBase`,
   `ShortcutsSettingsViewModel`) with `IconKey`. The WPF `ui:FontIcon` binding
   goes through a converter mapping `IconKey` → the same iNKORE glyph as
   today, so behavior is unchanged. The WinUI3 app's equivalent mapping
   (`IconKey` → WinUI glyph) is built later, in the final icon-mapping phase
   (Phase 6 below), not now.
2. **`WindowRenameViewModel` off `DependencyObject`.** Rewrite from
   `DependencyObject`/`DependencyProperty` to `ReactiveObject` with
   `[Reactive]` properties for `WindowItem` and `NewTitle`. No WPF type left
   in this class.
3. **Abstract app shutdown.** `NotifyIconViewModel.ExitApplication()` calls
   `Application.Current.Shutdown()` directly today. Add a small
   `IAppLifecycle` (naming TBD at implementation time) with a `Shutdown()`
   method; the WPF app registers an implementation that calls
   `Application.Current.Shutdown()`; `NotifyIconViewModel` depends on the
   interface instead.
4. **Remove the SysColors tray menu entry.** `NotifyIconViewModel`
   constructs a WPF `SysColor` window directly
   (`new SysColor().ShowDialog()`), which is a View/ViewModel layering
   violation independent of this migration. Remove the menu item that
   exposes `SysColorsCommand` from the tray context menu. Leave
   `SysColorsCommand`, the `SysColor` window, and the ViewModel method in
   place, unreferenced by the UI, for a future revisit — do not delete them.

`WindowSelectorViewModel`'s use of `System.Windows.Forms.Screen` is not a WPF
dependency (WinForms can be referenced from a WinUI 3 project too) and is not
touched by this cleanup.

After Phase 0, nothing in `WinTabberUI/ViewModels/**` references
`System.Windows.*` or any iNKORE type.

## Project structure

- **New `WinTabber.ViewModels` project** (plain C#, references neither WPF
  nor WinUI): everything currently under `WinTabberUI/ViewModels/**` moves
  here. Both the WPF app and the new WinUI3 app reference it.
- **New `winui3/` folder** at the repo root, containing:
  - `winui3/WinTabberUI/WinTabberUI.csproj` — the WinUI 3 desktop app
    (`net10.0-windows10.0.26100.0`, `<UseWinUI>true</UseWinUI>`,
    `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`)
  - `winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
  - `winui3/WinTabber.UI.Media/WinTabber.UI.Media.csproj`
  - `winui3/WinTabber.UI.Common.Tests`,
    `winui3/WinTabber.UI.Media.Tests` — mirror today's coverage in the WPF
    equivalents (for example `HintBehaviorTests`)
  - All three (five with tests) added to `WinTabber.slnx`
- Projects with no WPF dependency stay exactly as they are, referenced by
  both apps unchanged: `WinTabber.Api.Windowing`, `WinTabber.Api.Media`,
  `WinTabber.Events`, `WinTabber.Interop`, `WinTabber.Infrastructure`,
  `WinTabber.Common.Util`, and (after Phase 0) `WinTabber.ViewModels`.
- The work happens on a feature branch, not directly on `master`.

## Windows, backdrop, and overlay behavior

Each of the six windows becomes a WinUI 3 `Window` deriving from
`WinUIEx.WindowEx` (adds `PackageReference Include="WinUIEx"`), which restores
WPF-like windowing properties (`IsResizable`, `IsShownInSwitchers`,
`IsTitleBarVisible`, `SystemBackdrop`) as XAML instead of hand-written
`AppWindow`/`OverlappedPresenter` code-behind.

| Window | Backdrop | Notes |
|---|---|---|
| `SettingsWindow` | `MicaBackdrop` | Matches current `ui:WindowHelper.SystemBackdropType="Mica"`. `NavigationView`/`Frame` page navigation carries over structurally. |
| `WindowSelectorWindow` | `DesktopAcrylicBackdrop` | Matches current `AcrylicChrome`. No title bar, `IsShownInSwitchers = false`. |
| `DockWindow` | `DesktopAcrylicBackdrop` | Same as above. |
| `SuspendedWindowsWindow` | `DesktopAcrylicBackdrop` | Same as above. |
| `MediaControlsWindow` | `DesktopAcrylicBackdrop` | Same as above. |
| `ThumbnailWindow` | none | Matches today — no blur, so it does not obscure the DWM thumbnail rendered onto it via `DwmRegisterThumbnail`. |

All of `WinTabber.UI.Common/Chrome/` (`AcrylicChrome`, `AccentHelper`,
`CornerHelper`, `CustomChrome`, `PeekHelper`, `CaptionButtons`,
`AccentPolicy`/`AccentState`/`AccentFlags`, `ColorFrom`, `CloakHelper`,
`UsingColors`) is not carried into `winui3/WinTabber.UI.Common` — none of it
is needed once `SystemBackdrop` and the default WinUI 3 window frame take
over. It stays untouched in the WPF copy.

## Control and package migration

- `ui:SettingsCard` / `ui:SettingsExpander` →
  `CommunityToolkit.WinUI.Controls.SettingsControls`.
- `ui:ToggleSwitch`, `ui:FontIcon`, `ui:ContentDialog`, `ui:NavigationView`,
  `ui:Frame` → native WinUI 3 `ToggleSwitch`, `FontIcon`, `ContentDialog`,
  `NavigationView`, `Frame`.
- `ui:ThemeResources` / `ui:XamlControlsResources` → `XamlControlsResources`
  as the first merged dictionary in the new app's `App.xaml`.
- `{x:Static ui:FluentSystemIcons.*}` / `{x:Static ui:SegoeFluentIcons.*}`
  glyph references (~15 sites across `ShortcutsSettingsPage.xaml`,
  `SettingsWindow.xaml`, `MediaControlsWindow.xaml`): each becomes a generic
  placeholder glyph with a code comment naming the original iNKORE icon key.
  Resolved for real in Phase 6, not before.
- `ui:TextBoxHelper.IsDeleteButtonVisible="False"` sites: removed outright.
  Native `TextBox` has no delete-button chrome to suppress; this is a no-op
  removal, not a replacement.
- `H.NotifyIcon.Wpf` → `H.NotifyIcon.WinUI` for the tray icon.
- `ReactivePage` / `ReactiveWindow` / `ReactiveUserControl` → their
  `ReactiveUI.WinUI` package equivalents.
- Every `HwndSource` / `PresentationSource.FromVisual` call site in the app
  (outside the deleted `Chrome` folder) → `WinRT.Interop.WindowNative.GetWindowHandle`
  against the `WindowEx` instance.

## Testing

`winui3/WinTabber.UI.Common.Tests` and `winui3/WinTabber.UI.Media.Tests`
mirror today's coverage in the WPF equivalents. Beyond unit-level coverage,
verification is manual, side-by-side comparison against the running WPF
app — window positioning, blur/backdrop appearance, overlay show/hide
timing, and thumbnail rendering are not well-suited to automated tests.

## What this does not fix

A prior debugging session recorded that a WPF window cannot fade with a DWM
backdrop (see project memory `wpf-window-fade-limits`). Native
`SystemBackdrop` is still DWM-composited, so this migration should not be
assumed to unlock that animation — if it turns out to matter, it needs its
own investigation, not an assumption baked into this plan.

## Phase order

0. WPF-only ViewModel cleanup (above) — ships independently, verified by
   existing tests.
1. Scaffold the three `winui3/` projects and `WinTabber.ViewModels`; empty
   shell app boots.
2. Port `WinTabber.UI.Common` (behaviors, converters, controls) off iNKORE.
3. Convert `SettingsWindow` — closest to native already (NavigationView /
   Frame / Mica).
4. Convert the five overlay/media windows and their backdrops.
5. Tray icon + app bootstrap parity (DI wiring, startup sequencing).
6. Icon-key mapping pass — replace every placeholder glyph from the control
   migration step with a real WinUI equivalent, or a deliberate final
   placeholder if no equivalent exists.
7. Side-by-side manual verification pass across all six windows.

Detailed, file-level steps for each phase belong in the implementation plan,
not this document.
