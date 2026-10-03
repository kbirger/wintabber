# Tray icon: WinUI 3 port design

Date: 2026-09-18
Status: proposed

## Purpose

The WPF app shows a system tray icon (`NotifyIconCoordinator`, using
`H.NotifyIcon.Wpf`'s `TaskbarIcon`) with a right-click menu: Show Window,
Settings, Enable Hooks (toggle), Media debug view (toggle), Resume all
suspended, Exit. The winui3 app has no tray icon at all yet. This is the
first of two Phase 5 sub-projects (tray icon, then the missing window
coordinators) needed for full app bootstrap parity.

## Scope

Port the tray icon and its menu to winui3, using `H.NotifyIcon.WinUI` (the
same library family the WPF app already uses, targeting WinUI 3 instead of
WPF) rather than a raw Win32 `Shell_NotifyIcon` implementation — this keeps
the already-proven `TaskbarIcon`-based approach and lets the existing,
already-shared `NotifyIconViewModel` port with no changes.

The menu ships with 5 of the WPF original's 6 items. "Media debug view" is
deliberately excluded from this pass: it toggles `MediaDebugStateService`
state that nothing in winui3 listens to yet (`MediaDebugWindow` is not
ported), so including it would ship a checkbox with no observable effect.
It can be added back once `MediaDebugWindow` is wired up.

"Show Window" and "Settings" ARE included, sending the same
`WinTabberEventManager` events (`CmdNextWindow`/`CmdShowSettings`) the WPF
menu sends — but nothing in winui3 subscribes to either event yet (this is
exactly the coordinators gap the next Phase 5 sub-project closes: neither
`WindowSelectorWindow` nor `SettingsWindow` has a coordinator wiring it to a
global event in winui3 today). This is an accepted, explicit, temporary gap:
those two menu items will be visibly inert until the coordinators task
lands immediately after this one.

## Already-shared, unmodified

- `WinTabber.ViewModels/NotifyIconViewModel.cs` — framework-free, all of its
  constructor dependencies (`WinTabberEventManager`, `IProcessSuspensionService`,
  `MediaDebugStateService`, `IAppLifecycle`, `ISysColorsWindowLauncher`) are
  either already shared or are the interfaces this design implements for
  winui3 (below). No changes needed.
- `WinTabber.ViewModels/Services/IAppLifecycle.cs` and
  `ISysColorsWindowLauncher` — already framework-free interfaces; only their
  WPF implementations (`WpfAppLifecycle`, `WpfSysColorsWindowLauncher`) are
  WPF-specific and need winui3 counterparts.

## New files

- `winui3/WinTabberUI/Coordinators/NotifyIconCoordinator.cs` — constructs a
  `TaskbarIcon` (from `H.NotifyIcon.WinUI`), sets its icon source and
  tooltip, binds `LeftClickCommand` to `NotifyIconViewModel.ShowWindowCommand`,
  and attaches a `MenuFlyout` loaded from the resource below, with
  `DataContext` set to the injected `NotifyIconViewModel`. Structurally
  identical to the WPF `NotifyIconCoordinator`, using WinUI 3's flyout types
  in place of WPF's menu types.
- `winui3/WinTabberUI/Resources/NotifyIconResources.xaml` — a `MenuFlyout`
  resource:

  ```xml
  <MenuFlyout x:Key="SysTrayMenu">
      <MenuFlyoutItem Text="Show Window" Command="{Binding ShowWindowCommand}" />
      <MenuFlyoutItem Text="Settings" Command="{Binding ShowSettingsCommand}" />
      <ToggleMenuFlyoutItem Text="Enable Hooks" Command="{Binding PauseHooksCommand}" IsChecked="{Binding AreHooksActive, Mode=OneWay}" />
      <MenuFlyoutItem Text="Resume all suspended" Command="{Binding ResumeAllSuspendedCommand}" />
      <MenuFlyoutItem Text="Exit" Command="{Binding ExitApplicationCommand}" />
  </MenuFlyout>
  ```

  `MenuFlyoutItem`/`ToggleMenuFlyoutItem` are WinUI 3's direct equivalents
  of WPF's `MenuItem`/checkable `MenuItem` — `ToggleMenuFlyoutItem` confirmed
  to exist in the Windows App SDK's own `Microsoft.UI.Xaml.winmd` metadata,
  not assumed. `ReactiveCommand<Unit,Unit>` already implements `ICommand`, so
  these bindings work exactly like every other ported window's command
  bindings.
- `winui3/WinTabberUI/Services/WinUIAppLifecycle.cs`:

  ```csharp
  public sealed class WinUIAppLifecycle : IAppLifecycle
  {
      public void Shutdown() => Microsoft.UI.Xaml.Application.Current.Exit();
  }
  ```

- `winui3/WinTabberUI/Services/WinUISysColorsWindowLauncher.cs`:

  ```csharp
  public sealed class WinUISysColorsWindowLauncher : ISysColorsWindowLauncher
  {
      public void Show() { }
  }
  ```

  A one-line, honest no-op: `NotifyIconViewModel` requires this dependency
  to construct (its `SysColorsCommand` exists even though the WPF menu
  itself never exposes it either), and the SysColors dialog itself is a
  deliberately deferred port (Task 0.4). Not a new gap.
- `winui3/WinTabberUI/Assets/logo.ico` — copied from
  `WinTabberUI/Images/logo.ico`, the tray icon's image source. The winui3
  app has no `Assets/` folder yet; this creates it.

## Startup lifecycle change

`App.xaml.cs`'s `OnLaunched` currently constructs and shows `SettingsWindow`
directly on every launch — a temporary stand-in, by its own doc comment,
until a real tray-app startup lifecycle exists. This task removes that
auto-show: `OnLaunched` resolves and activates `NotifyIconCoordinator` (see
below) and stops there, so the app starts quietly in the tray instead of
always popping a visible window.

Accepted, explicit trade-off: until the coordinators sub-project (next)
wires "Show Window"/"Settings" to their events, there is no way to open any
window from the running app at all — not even `SettingsWindow`, which this
task removes the only current path to. This is intentional: the point of
this change is to reach the real "quiet tray app" starting shape now,
rather than deferring it alongside the coordinators work.

## Package reference

Add `H.NotifyIcon.WinUI` to `winui3/WinTabberUI/WinTabberUI.csproj`.

## DI and startup wiring

In `Bootstrapper.cs`, add a new `AddTrayIconGraph()` extension method
(following the established per-window/per-feature grouping convention:
`AddSettingsGraph`, `AddMediaControlsGraph`, etc.), registering
`NotifyIconCoordinator`, `NotifyIconViewModel`,
`IAppLifecycle`→`WinUIAppLifecycle`, and
`ISysColorsWindowLauncher`→`WinUISysColorsWindowLauncher` as singletons.

In `App.xaml.cs`'s `OnLaunched`, resolve `NotifyIconCoordinator` eagerly —
the same simple pattern `ThumbnailWindowCoordinator`/
`MediaControlsWindowCoordinator` already use. Not the WPF app's
`BackgroundServiceContainer` disposal-ordering pattern: that full-parity
piece belongs to the coordinators sub-project that follows this one, not to
the tray icon alone.

## Explicit exclusions

- "Media debug view" menu item — deferred until `MediaDebugWindow` is
  wired up in winui3 (see Scope above).
- `BackgroundServiceContainer`'s ordered-disposal pattern — not ported in
  this task; the tray icon is resolved directly in `App.xaml.cs`, matching
  every other coordinator winui3 has today.
- Any change to `NotifyIconViewModel` itself, or to `IAppLifecycle`/
  `ISysColorsWindowLauncher` — all three are already correct and shared as-is.

## Testing

No unit tests: this is UI/DI wiring with no new logic (`NotifyIconViewModel`
is unmodified). Verification is manual: launch the app, confirm the tray
icon appears, right-click shows the 5-item menu, "Enable Hooks" reflects
and toggles real hook state, "Resume all suspended" and "Exit" behave
correctly, and "Show Window"/"Settings" are confirmed to be visibly inert
(expected, not a regression) pending the coordinators task.
