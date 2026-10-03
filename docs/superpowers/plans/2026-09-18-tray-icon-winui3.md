# Tray Icon WinUI 3 Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the winui3 app a system tray icon and menu, matching the WPF app's `NotifyIconCoordinator`, and stop the app auto-showing `SettingsWindow` at every launch.

**Architecture:** `H.NotifyIcon.WinUI`'s `TaskbarIcon` (the WinUI 3 build of the same library the WPF app already uses) hosts a `MenuFlyout` built directly in C# code, bound to the already-shared, unmodified `NotifyIconViewModel`. Two small stub services (`WinUIAppLifecycle`, `WinUISysColorsWindowLauncher`) satisfy that view model's remaining framework-specific dependencies. `App.xaml.cs`'s `OnLaunched` resolves the new coordinator instead of constructing `SettingsWindow`.

**Tech Stack:** WinUI 3 / Windows App SDK, `H.NotifyIcon.WinUI`, C#, ReactiveUI.

**Spec:** `docs/superpowers/specs/2026-09-18-tray-icon-winui3-design.md`

## Global Constraints

- Scope is the tray icon and the startup-lifecycle change only — no coordinator wiring for `WindowSelectorWindow`/`SettingsWindow` events in this plan (that is the next, separate sub-project).
- The menu has exactly 5 items: Show Window, Settings, Enable Hooks (toggle), Resume all suspended, Exit. "Media debug view" is explicitly excluded (its backing window isn't wired up in winui3 yet).
- `NotifyIconViewModel` (`WinTabber.ViewModels/NotifyIconViewModel.cs`) is not modified — it is already framework-free and correct as-is.
- "Show Window" and "Settings" menu items are expected to be visibly inert after this plan — accepted, not a bug to fix here.
- **Deviation from the spec's suggested implementation, noted here explicitly:** the spec sketched the menu as a `NotifyIconResources.xaml` XAML resource dictionary loaded via an `ms-appx:///` URI. This plan instead builds the `MenuFlyout` directly in C# code inside `NotifyIconCoordinator`, for one concrete reason: this codebase has confirmed, working precedent for `ms-appx:///<AssemblyName>/...` cross-assembly XAML resource loading (`ShortcutCaptureDialog.xaml`, `ShortcutsSettingsPage.xaml`) and for XAML-relative same-assembly merging (`DockWindow.xaml`'s `Source="/Resources/DockWindowResources.xaml"`), but no confirmed precedent for loading a same-assembly XAML resource dictionary imperatively from C# code — an untested combination this plan avoids rather than assumes. Building the `MenuFlyout` in code sidesteps that unverified URI-resolution question entirely, matches the WPF `NotifyIconCoordinator`'s own already-imperative `BindingOperations.SetBinding` pattern, and produces an identical menu (same 5 items, same commands, same toggle). The observable result is unchanged from the spec's design; only the mechanism differs.

---

## File Structure

- Modify `Directory.Packages.props` — add the `H.NotifyIcon.WinUI` package version.
- Modify `winui3/WinTabberUI/WinTabberUI.csproj` — add the `H.NotifyIcon.WinUI` package reference.
- Create `winui3/WinTabberUI/Assets/logo.ico` — copied from `WinTabberUI/Images/logo.ico`.
- Create `winui3/WinTabberUI/Services/WinUIAppLifecycle.cs` — `IAppLifecycle` implementation.
- Create `winui3/WinTabberUI/Services/WinUISysColorsWindowLauncher.cs` — `ISysColorsWindowLauncher` no-op implementation.
- Create `winui3/WinTabberUI/Coordinators/NotifyIconCoordinator.cs` — builds and shows the tray icon and its menu.
- Modify `winui3/WinTabberUI/Bootstrapper.cs` — add `AddTrayIconGraph()`.
- Modify `winui3/WinTabberUI/App.xaml.cs` — resolve `NotifyIconCoordinator`, stop auto-showing `SettingsWindow`.

## Testing approach

No unit tests: this is DI/UI wiring with no new logic (`NotifyIconViewModel` is unmodified, and both new stub services are one-line pass-throughs to framework APIs). Each task below is verified by a successful build plus, for the two integration tasks (3 and 4), a manual launch check — the same approach the hint-overlay plan used for its own framework-wiring tasks, since this repo currently has no automated WinUI 3 desktop UI-test harness.

---

### Task 1: Add the `H.NotifyIcon.WinUI` package and the tray icon asset

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `winui3/WinTabberUI/WinTabberUI.csproj`
- Create: `winui3/WinTabberUI/Assets/logo.ico`

**Interfaces:**
- Produces: the `H.NotifyIcon.WinUI` package available to `winui3/WinTabberUI`, and an icon file at `Assets/logo.ico` that Task 3 references by URI.

- [ ] **Step 1: Add the package version**

In `Directory.Packages.props`, find the line:

```xml
<PackageVersion Include="H.NotifyIcon.Wpf" Version="2.4.1" />
```

Add a new line directly after it:

```xml
<PackageVersion Include="H.NotifyIcon.WinUI" Version="2.4.1" />
```

- [ ] **Step 2: Add the package reference**

In `winui3/WinTabberUI/WinTabberUI.csproj`, find:

```xml
        <PackageReference Include="CommunityToolkit.WinUI.Controls.Primitives" />
    </ItemGroup>
```

Replace with:

```xml
        <PackageReference Include="CommunityToolkit.WinUI.Controls.Primitives" />
        <PackageReference Include="H.NotifyIcon.WinUI" />
    </ItemGroup>
```

- [ ] **Step 3: Copy the icon asset**

Copy the file `WinTabberUI/Images/logo.ico` to a new file at
`winui3/WinTabberUI/Assets/logo.ico` (the `Assets/` folder does not exist
yet in the winui3 project — create it). Use a plain file copy (e.g.
`cp WinTabberUI/Images/logo.ico winui3/WinTabberUI/Assets/logo.ico` from
the repo root), not a re-encode or resize — it is a binary `.ico` file.

- [ ] **Step 4: Restore and build**

Run: `dotnet restore winui3/WinTabberUI/WinTabberUI.csproj` then
`dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors. (`H.NotifyIcon.WinUI` is not yet
referenced by any code, so this only proves the package restores and the
project still compiles.)

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props winui3/WinTabberUI/WinTabberUI.csproj winui3/WinTabberUI/Assets/logo.ico
git commit -m "chore: add H.NotifyIcon.WinUI package and tray icon asset"
```

---

### Task 2: `WinUIAppLifecycle` and `WinUISysColorsWindowLauncher` stub services

**Files:**
- Create: `winui3/WinTabberUI/Services/WinUIAppLifecycle.cs`
- Create: `winui3/WinTabberUI/Services/WinUISysColorsWindowLauncher.cs`

**Interfaces:**
- Consumes: `WinTabber.ViewModels.Services.IAppLifecycle` and `ISysColorsWindowLauncher` (both already defined, framework-free, unmodified by this plan).
- Produces: `WinUIAppLifecycle : IAppLifecycle` and `WinUISysColorsWindowLauncher : ISysColorsWindowLauncher` — Task 4 registers both in DI.

- [ ] **Step 1: Create `WinUIAppLifecycle`**

Create `winui3/WinTabberUI/Services/WinUIAppLifecycle.cs`:

```csharp
namespace WinTabberUI.Services;

public sealed class WinUIAppLifecycle : IAppLifecycle
{
    public void Shutdown() => Microsoft.UI.Xaml.Application.Current.Exit();
}
```

- [ ] **Step 2: Create `WinUISysColorsWindowLauncher`**

Create `winui3/WinTabberUI/Services/WinUISysColorsWindowLauncher.cs`:

```csharp
namespace WinTabberUI.Services;

/// <summary>
/// No-op: the SysColors dialog itself is a deliberately deferred port (Task 0.4). This satisfies
/// NotifyIconViewModel's constructor dependency; SysColorsCommand is never exposed in the tray
/// menu, matching the WPF app's own menu, which defines the command but never shows it either.
/// </summary>
public sealed class WinUISysColorsWindowLauncher : ISysColorsWindowLauncher
{
    public void Show() { }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors. (Both classes are unreferenced by DI
until Task 4, so this only proves they compile against the shared
interfaces.)

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabberUI/Services/WinUIAppLifecycle.cs winui3/WinTabberUI/Services/WinUISysColorsWindowLauncher.cs
git commit -m "feat: add winui3 app-lifecycle and sys-colors stub services"
```

---

### Task 3: `NotifyIconCoordinator`

**Files:**
- Create: `winui3/WinTabberUI/Coordinators/NotifyIconCoordinator.cs`

**Interfaces:**
- Consumes: `WinTabber.ViewModels.NotifyIconViewModel` (constructor-injected; its public `ReactiveCommand<Unit,Unit>` properties are `ShowWindowCommand`, `ShowSettingsCommand`, `PauseHooksCommand`, `ResumeAllSuspendedCommand`, `ExitApplicationCommand`, and its `bool AreHooksActive` property — all already defined in `WinTabber.ViewModels/NotifyIconViewModel.cs`, unmodified by this plan).
- Produces: `NotifyIconCoordinator` (constructor takes `NotifyIconViewModel`) — Task 4 registers it as a singleton and resolves it eagerly in `App.xaml.cs`.

- [ ] **Step 1: Create the coordinator**

Create `winui3/WinTabberUI/Coordinators/NotifyIconCoordinator.cs`:

```csharp
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using WinTabber.ViewModels;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Ported from the WPF app's NotifyIconCoordinator. The menu is built directly in code rather
/// than loaded from a XAML resource dictionary -- see the "Deviation" note in this plan's Global
/// Constraints for why: this app has confirmed precedent for ms-appx:/// XAML resource loading in
/// declarative XAML markup, but none for loading a same-assembly resource dictionary imperatively
/// from C#, so building the menu in code avoids an unverified URI-resolution question rather than
/// assuming it works the same way.
/// </summary>
public sealed class NotifyIconCoordinator : IDisposable
{
    private readonly TaskbarIcon _view;

    public NotifyIconCoordinator(NotifyIconViewModel vm)
    {
        var menu = new MenuFlyout();

        var showWindowItem = new MenuFlyoutItem { Text = "Show Window" };
        BindingOperations.SetBinding(showWindowItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ShowWindowCommand)),
        });
        menu.Items.Add(showWindowItem);

        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        BindingOperations.SetBinding(settingsItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ShowSettingsCommand)),
        });
        menu.Items.Add(settingsItem);

        var enableHooksItem = new ToggleMenuFlyoutItem { Text = "Enable Hooks" };
        BindingOperations.SetBinding(enableHooksItem, ToggleMenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.PauseHooksCommand)),
        });
        BindingOperations.SetBinding(enableHooksItem, ToggleMenuFlyoutItem.IsCheckedProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.AreHooksActive)),
            Mode = BindingMode.OneWay,
        });
        menu.Items.Add(enableHooksItem);

        var resumeAllItem = new MenuFlyoutItem { Text = "Resume all suspended" };
        BindingOperations.SetBinding(resumeAllItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ResumeAllSuspendedCommand)),
        });
        menu.Items.Add(resumeAllItem);

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        BindingOperations.SetBinding(exitItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ExitApplicationCommand)),
        });
        menu.Items.Add(exitItem);

        foreach (var item in menu.Items)
        {
            ((FrameworkElement)item).DataContext = vm;
        }

        var view = new TaskbarIcon
        {
            DataContext = vm,
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/logo.ico")),
            ToolTipText = "WinTabber",
            ContextFlyout = menu,
        };
        BindingOperations.SetBinding(view, TaskbarIcon.LeftClickCommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ShowWindowCommand)),
        });
        view.ForceCreate();

        _view = view;
    }

    public void Dispose()
    {
        _view?.Dispose();
    }
}
```

Note: `MenuFlyoutItem`/`ToggleMenuFlyoutItem` do not inherit `DataContext`
from their parent `MenuFlyout` automatically the way a visual tree does, so
each item's `DataContext` is set explicitly in the `foreach` loop above —
without this, the `Binding`s on each item would resolve against a null
data context and never find `vm`'s commands. If the build reports
`FrameworkElement` is ambiguous or missing a `using`, add
`using Microsoft.UI.Xaml;` to this file's usings.

- [ ] **Step 2: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors. If a specific `H.NotifyIcon.WinUI`
API name in this code doesn't match what the installed package version
exposes (e.g. `ContextFlyout` vs a differently-named property, or
`ForceCreate` not existing), use the build error and your IDE's
IntelliSense against the actual installed package to find the correct
member name, and use that instead — this is a real third-party package
surface this plan cannot fully verify without compiling against it.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabberUI/Coordinators/NotifyIconCoordinator.cs
git commit -m "feat: add winui3 NotifyIconCoordinator with tray menu"
```

---

### Task 4: Wire the tray icon into DI and startup, stop auto-showing `SettingsWindow`

**Files:**
- Modify: `winui3/WinTabberUI/Bootstrapper.cs`
- Modify: `winui3/WinTabberUI/App.xaml.cs`

**Interfaces:**
- Consumes: `NotifyIconCoordinator` (Task 3), `WinUIAppLifecycle`/`WinUISysColorsWindowLauncher` (Task 2), `WinTabber.ViewModels.NotifyIconViewModel` (already defined).

- [ ] **Step 1: Add `AddTrayIconGraph` to `Bootstrapper.cs`**

In `winui3/WinTabberUI/Bootstrapper.cs`, no new `using` is needed: `IAppLifecycle`,
`ISysColorsWindowLauncher`, `WinUIAppLifecycle`, and `WinUISysColorsWindowLauncher`
all live in namespace `WinTabberUI.Services` (confirmed directly in both interface
files — a pre-existing quirk where these types live physically under
`WinTabber.ViewModels/Services/` but declare `namespace WinTabberUI.Services;`),
and `Bootstrapper.cs` already has `using WinTabberUI.Services;` at its top.

Find:

```csharp
    public static ServiceProvider Init()
    {
        return new ServiceCollection()
            .AddCoreServices()
            .AddSettingsGraph()
            .AddDockAndSuspendedWindowsGraph()
            .AddMediaControlsGraph()
            .AddWindowSelectorGraph()
            .AddThumbnailWindowGraph()
            .BuildServiceProvider();
    }
```

Replace with:

```csharp
    public static ServiceProvider Init()
    {
        return new ServiceCollection()
            .AddCoreServices()
            .AddSettingsGraph()
            .AddDockAndSuspendedWindowsGraph()
            .AddMediaControlsGraph()
            .AddWindowSelectorGraph()
            .AddThumbnailWindowGraph()
            .AddTrayIconGraph()
            .BuildServiceProvider();
    }
```

Then add this new method to the `Bootstrapper` class, after
`AddThumbnailWindowGraph`:

```csharp
    private static IServiceCollection AddTrayIconGraph(this IServiceCollection services)
    {
        return services
            .AddSingleton<IAppLifecycle, WinUIAppLifecycle>()
            .AddSingleton<ISysColorsWindowLauncher, WinUISysColorsWindowLauncher>()
            .AddSingleton<NotifyIconViewModel>()
            .AddSingleton<Coordinators.NotifyIconCoordinator>();
    }
```

- [ ] **Step 2: Wire it into `App.xaml.cs`, remove the `SettingsWindow` auto-show**

In `winui3/WinTabberUI/App.xaml.cs`, find:

```csharp
    private ThumbnailWindowCoordinator? _thumbnailWindowCoordinator;
    private MediaControlsWindowCoordinator? _mediaControlsWindowCoordinator;
```

Replace with:

```csharp
    private ThumbnailWindowCoordinator? _thumbnailWindowCoordinator;
    private MediaControlsWindowCoordinator? _mediaControlsWindowCoordinator;
    private NotifyIconCoordinator? _notifyIconCoordinator;
```

Then find:

```csharp
        _eventManager = Services.GetRequiredService<WinTabberEventManager>();
        _thumbnailWindowCoordinator = Services.GetRequiredService<ThumbnailWindowCoordinator>().Init();
        _mediaControlsWindowCoordinator = Services.GetRequiredService<MediaControlsWindowCoordinator>();
```

Replace with:

```csharp
        _eventManager = Services.GetRequiredService<WinTabberEventManager>();
        _thumbnailWindowCoordinator = Services.GetRequiredService<ThumbnailWindowCoordinator>().Init();
        _mediaControlsWindowCoordinator = Services.GetRequiredService<MediaControlsWindowCoordinator>();
        _notifyIconCoordinator = Services.GetRequiredService<NotifyIconCoordinator>();
```

Finally, find:

```csharp
        _window = new SettingsWindow(Services.GetRequiredService<SettingsViewModel>());
        _window.Activate();
    }
```

Replace with:

```csharp
        // No window is shown at launch: the app now starts quietly in the tray. SettingsWindow
        // and WindowSelectorWindow are shown on demand once their coordinators exist (a following,
        // separate task) -- until then there is intentionally no way to open a window from the
        // running app, per this plan's explicit startup-lifecycle scope change.
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors. If `_window`'s field declaration
(`private Window? _window;`), its type `Window`, `SettingsViewModel`, or
the `using WinTabberUI.Views;`/`using WinTabber.ViewModels;` lines are now
unused and the compiler only warns (not errors) about them, leave them in
place rather than deleting — a following task (coordinators) will use
`_window` again to show whichever window is requested, and removing the
usings now would just mean re-adding them in that task.

- [ ] **Step 4: Manual verification**

Kill any running instance (`taskkill //IM WinTabberUI.exe //F`), rebuild,
and launch `winui3\WinTabberUI\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\WinTabberUI.exe`.
Confirm: no window appears at launch. A tray icon appears in the system
tray. Right-clicking it shows a 5-item menu: Show Window, Settings, Enable
Hooks (checkbox), Resume all suspended, Exit. Click "Enable Hooks" and
confirm the checkbox reflects real hook state (test by triggering a global
hotkey before and after toggling it off). Click "Resume all suspended" and
confirm no crash (harmless no-op if nothing is suspended). Click "Exit"
and confirm the process actually terminates. Separately, confirm "Show
Window" and "Settings" do nothing when clicked — this is expected per
this plan's scope, not a bug.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabberUI/Bootstrapper.cs winui3/WinTabberUI/App.xaml.cs
git commit -m "feat: wire tray icon into winui3 startup, stop auto-showing SettingsWindow"
```

---

### Task 5: Update the migration progress ledger

**Files:**
- Modify: `.superpowers/sdd/2026-09-12-wpf-to-winui3-migration/progress.md` (gitignored; not committed)

- [ ] **Step 1: Append an entry**

Add an entry noting: tray icon ported to winui3 using `H.NotifyIcon.WinUI`,
menu built in code (not XAML resource) to avoid an unverified `ms-appx:///`
same-assembly code-loading question; `App.xaml.cs` no longer auto-shows
`SettingsWindow` at launch, so the app now starts quietly in the tray;
"Show Window"/"Settings" menu items are confirmed inert pending the next
sub-project (coordinators); "Media debug view" menu item deliberately
excluded pending `MediaDebugWindow`'s own winui3 port.

- [ ] **Step 2: No commit** (ledger is gitignored).

---

## Self-Review

**Spec coverage:**
- Library choice (`H.NotifyIcon.WinUI`) → Task 1.
- Menu scope (5 items, "Media debug view" excluded) → Task 3.
- `WinUIAppLifecycle`/`WinUISysColorsWindowLauncher` stubs → Task 2.
- DI wiring (`AddTrayIconGraph`) and eager resolution in `App.xaml.cs` → Task 4.
- Startup-lifecycle change (stop auto-showing `SettingsWindow`) → Task 4.
- Explicit exclusions (`BackgroundServiceContainer`, `NotifyIconViewModel` changes, "Media debug view") → nothing in this plan touches or references them; covered by omission, called out in Global Constraints.
- The spec's suggested XAML-resource menu implementation → deliberately deviated from, with the reasoning stated up front in Global Constraints rather than silently changed.

**Placeholder scan:** No TBD/TODO markers. Every step has literal code, except Task 3 Step 2's explicit acknowledgment that an unverified third-party package's exact member names may need a build-driven correction — this is a disclosed, bounded uncertainty about an external API surface, not an unwritten requirement.

**Type consistency:** `NotifyIconCoordinator(NotifyIconViewModel vm)` is defined once in Task 3 and registered/resolved with that exact type in Task 4. `IAppLifecycle`/`WinUIAppLifecycle` and `ISysColorsWindowLauncher`/`WinUISysColorsWindowLauncher` are defined in Task 2 and registered with those exact names in Task 4.
