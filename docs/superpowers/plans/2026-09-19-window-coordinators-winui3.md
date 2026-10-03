# Window Coordinators WinUI 3 Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `WindowSelectorWindow` and `SettingsWindow` actually appear when the tray icon's "Show Window"/"Settings" menu items (or left-click, for Show Window) are used — both currently send events nothing subscribes to in winui3.

**Architecture:** Two new coordinators, each following `MediaControlsWindowCoordinator`'s exact shape: subscribe to an already-shared, already-working `IObservable<bool>` on the window's view model, call `Show()`/`Hide()` on the window, and force it to the foreground since the trigger arrives through a global hook. `WindowSelectorWindow`'s DI lifetime changes from transient to singleton (it is a single reused switcher, matching the WPF original); `SettingsWindow` is registered transient for the first time (it doesn't exist in winui3's DI container yet).

**Tech Stack:** WinUI 3 / Windows App SDK, ReactiveUI, C#, Microsoft.Extensions.DependencyInjection.

**Spec:** `docs/superpowers/specs/2026-09-19-window-coordinators-winui3-design.md`

## Global Constraints

- `WindowSelectorViewModel.IsSwitcherActiveChanges` and `SettingsViewModel.IsSettingsShown` (in `SettingsWindowViewModel.cs` — file name doesn't match the class name, a pre-existing quirk) are not modified — both are already correct and shared.
- `ViewCoordinatorBase<T>` is not ported — both coordinators use the plain-class-plus-subscription shape already established by `ThumbnailWindowCoordinator`/`MediaControlsWindowCoordinator`.
- `WindowSelectorWindow`'s DI registration changes from `AddTransient` to `AddSingleton` in `Bootstrapper.cs`'s `AddWindowSelectorGraph`.
- `SettingsWindow` gets a new `AddTransient` registration in `AddSettingsGraph` (it has none today).
- `SettingsWindowCoordinator` must call `vm.Hide()` when the window is closed externally (its own titlebar close button), or `IsSettingsShown` gets stuck `true` — this is a deliberate port of the WPF original's own correctness fix, not new scope.
- No unit tests: DI/UI wiring with no new logic in the view models themselves. Verified manually (see each task).

---

## File Structure

- Create `winui3/WinTabberUI/Coordinators/WindowSelectorWindowCoordinator.cs`.
- Create `winui3/WinTabberUI/Coordinators/SettingsWindowCoordinator.cs`.
- Modify `winui3/WinTabberUI/Bootstrapper.cs` — `AddWindowSelectorGraph` (transient → singleton, add coordinator registration), `AddSettingsGraph` (add `SettingsWindow` + coordinator registrations).
- Modify `winui3/WinTabberUI/App.xaml.cs` — resolve both coordinators eagerly in `OnLaunched`; remove the now-fully-unused `_window` field (each coordinator manages its own window instance internally, so the App-level field has no remaining purpose).

---

### Task 1: `WindowSelectorWindowCoordinator`

**Files:**
- Create: `winui3/WinTabberUI/Coordinators/WindowSelectorWindowCoordinator.cs`

**Interfaces:**
- Consumes: `WindowSelectorViewModel.IsSwitcherActiveChanges` (`WinTabber.ViewModels/WindowSelectorViewModel.cs`, already exists, `IObservable<bool>`, unmodified); `IWindowInterop.ForceForeground(int)` (already exists, used identically by `MediaControlsWindowCoordinator`); `Views.WindowSelectorWindow(WindowSelectorViewModel, ApplicationSettings)` (already exists, both constructor params already registered in DI).
- Produces: `WindowSelectorWindowCoordinator(WindowSelectorViewModel, IServiceProvider, IWindowInterop)` — Task 3 registers and resolves this.

- [ ] **Step 1: Create the coordinator**

Create `winui3/WinTabberUI/Coordinators/WindowSelectorWindowCoordinator.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Interop;
using WinTabber.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows or hides the singleton <see cref="WindowSelectorWindow"/> whenever
/// <see cref="WindowSelectorViewModel.IsSwitcherActiveChanges"/> changes. Follows
/// <see cref="MediaControlsWindowCoordinator"/>'s established shape, not the WPF original's
/// <c>ViewCoordinatorBase&lt;T&gt;</c> (not ported in this migration -- see that coordinator's own
/// doc comment).
/// </summary>
/// <remarks>
/// Singleton, matching the WPF original's <c>WindowSelectorViewCoordinator</c>, which explicitly
/// sets <c>ReuseInstances = true</c>: this is a single global switcher shown/hidden repeatedly,
/// not rebuilt per show.
/// <para>
/// The hotkey/tray-click arrives through a global hook, so this process holds no foreground right
/// and <c>Show()</c> alone would not bring the window to the front -- ported from
/// <see cref="MediaControlsWindowCoordinator"/>'s own <c>ForceForeground</c> step.
/// </para>
/// </remarks>
public class WindowSelectorWindowCoordinator : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWindowInterop _interop;
    private readonly IDisposable _subscription;
    private WindowSelectorWindow? _window;

    public WindowSelectorWindowCoordinator(
        WindowSelectorViewModel vm,
        IServiceProvider serviceProvider,
        IWindowInterop interop)
    {
        _serviceProvider = serviceProvider;
        _interop = interop;

        _subscription = vm.IsSwitcherActiveChanges
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isActive =>
            {
                if (isActive)
                {
                    ShowWindow();
                }
                else
                {
                    _window?.Hide();
                }
            });
    }

    private void ShowWindow()
    {
        _window ??= _serviceProvider.GetRequiredService<WindowSelectorWindow>();
        _window.Show();

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (handle == nint.Zero)
        {
            return;
        }

        _interop.ForceForeground((int)handle);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors. (Unreferenced by DI until Task 3, so this only proves it compiles against the real `WindowSelectorViewModel`/`IWindowInterop`/`WindowSelectorWindow` types.)

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabberUI/Coordinators/WindowSelectorWindowCoordinator.cs
git commit -m "feat: add WindowSelectorWindowCoordinator"
```

---

### Task 2: `SettingsWindowCoordinator`

**Files:**
- Create: `winui3/WinTabberUI/Coordinators/SettingsWindowCoordinator.cs`

**Interfaces:**
- Consumes: `SettingsViewModel.IsSettingsShown` (`WinTabber.ViewModels/SettingsWindowViewModel.cs` — class is `SettingsViewModel`, file name doesn't match, pre-existing, already exists, `IObservable<bool>`, unmodified) and `SettingsViewModel.Hide()` (already exists, public, unmodified); `Views.SettingsWindow(SettingsViewModel)` (already exists).
- Produces: `SettingsWindowCoordinator(SettingsViewModel, IServiceProvider, IWindowInterop)` — Task 3 registers and resolves this.

- [ ] **Step 1: Create the coordinator**

Create `winui3/WinTabberUI/Coordinators/SettingsWindowCoordinator.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Interop;
using WinTabber.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows or hides <see cref="SettingsWindow"/> whenever <see cref="SettingsViewModel.IsSettingsShown"/>
/// changes. Follows <see cref="MediaControlsWindowCoordinator"/>'s established shape.
/// </summary>
/// <remarks>
/// Transient, matching the WPF original's <c>SettingsWindowViewCoordinator</c>, which explicitly
/// sets <c>ReuseInstances = false</c>: a fresh window each time, not reused like the switcher.
/// <para>
/// Unlike <see cref="WindowSelectorWindow"/>, the user can close this window directly via its own
/// titlebar. Without the <see cref="Window.Closed"/> handler below, <c>IsSettingsShown</c> would
/// stay stuck <c>true</c> after such a close -- ported from the WPF original's own
/// <c>Instance_Closed</c> handler, not new scope.
/// </para>
/// </remarks>
public class SettingsWindowCoordinator : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWindowInterop _interop;
    private readonly SettingsViewModel _vm;
    private readonly IDisposable _subscription;
    private SettingsWindow? _window;

    public SettingsWindowCoordinator(
        SettingsViewModel vm,
        IServiceProvider serviceProvider,
        IWindowInterop interop)
    {
        _vm = vm;
        _serviceProvider = serviceProvider;
        _interop = interop;

        _subscription = vm.IsSettingsShown
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isShown =>
            {
                if (isShown)
                {
                    ShowWindow();
                }
                else
                {
                    _window?.Close();
                }
            });
    }

    private void ShowWindow()
    {
        _window = _serviceProvider.GetRequiredService<SettingsWindow>();
        _window.Closed += OnWindowClosed;
        _window.Show();

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (handle == nint.Zero)
        {
            return;
        }

        _interop.ForceForeground((int)handle);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _window!.Closed -= OnWindowClosed;
        _window = null;
        _vm.Hide();
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors. `WindowEventArgs` was confirmed present in this SDK's own
metadata before writing this plan, so the signature above should compile as written.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabberUI/Coordinators/SettingsWindowCoordinator.cs
git commit -m "feat: add SettingsWindowCoordinator"
```

---

### Task 3: Wire both coordinators into DI and startup

**Files:**
- Modify: `winui3/WinTabberUI/Bootstrapper.cs`
- Modify: `winui3/WinTabberUI/App.xaml.cs`

**Interfaces:**
- Consumes: `WindowSelectorWindowCoordinator` (Task 1), `SettingsWindowCoordinator` (Task 2).

- [ ] **Step 1: Change `WindowSelectorWindow` to singleton, add its coordinator**

In `winui3/WinTabberUI/Bootstrapper.cs`, find:

```csharp
    private static IServiceCollection AddWindowSelectorGraph(this IServiceCollection services)
    {
        return services
            .AddSingleton<IActiveWindowStateService, ActiveWindowStateService>()
            .AddSingleton<ApplicationStateViewModelFactory>()
            .AddSingleton(sp => sp.GetRequiredService<ApplicationStateViewModelFactory>().CreateApplicationStateViewModel())
            .AddSingleton<WindowSelectorViewModel>()
            // Transient, same reasoning as DockWindow/SuspendedWindowsWindow (Task 4a.4's fix, reapplied
            // to every window since): a WinUI 3 Window can only be shown once, so the container must
            // hand back a fresh instance on every resolve rather than a disposed singleton.
            .AddTransient<Views.WindowSelectorWindow>();
    }
```

Replace with:

```csharp
    private static IServiceCollection AddWindowSelectorGraph(this IServiceCollection services)
    {
        return services
            .AddSingleton<IActiveWindowStateService, ActiveWindowStateService>()
            .AddSingleton<ApplicationStateViewModelFactory>()
            .AddSingleton(sp => sp.GetRequiredService<ApplicationStateViewModelFactory>().CreateApplicationStateViewModel())
            .AddSingleton<WindowSelectorViewModel>()
            // Singleton, not transient like every other window registered before this one: unlike a
            // per-source-window ThumbnailWindow, this is a single global switcher shown/hidden
            // repeatedly by WindowSelectorWindowCoordinator, matching the WPF original's own
            // ReuseInstances = true -- see that coordinator's own doc comment.
            .AddSingleton<Views.WindowSelectorWindow>()
            // Singleton, rooted explicitly in App.xaml.cs's OnLaunched, same reasoning as
            // ThumbnailWindowCoordinator: its subscription to WindowSelectorViewModel must stay
            // alive for the app's lifetime, not depend on incidental resolution order.
            .AddSingleton<Coordinators.WindowSelectorWindowCoordinator>();
    }
```

- [ ] **Step 2: Add `SettingsWindow` and its coordinator**

In the same file, find:

```csharp
    private static IServiceCollection AddSettingsGraph(this IServiceCollection services)
    {
        return services
            // Single shared instance: the settings page mutates this object and calls Save(), so a
            // second Load() elsewhere would silently diverge from what the user sees.
            .AddSingleton<ApplicationSettings>(_ => ApplicationSettings.Load())
            // The live keymap. Seeded from settings.json so the very first hotkey registration
            // already uses the user's bindings; the settings page pushes replacements on save.
            .AddSingleton<IShortcutMapProvider>(sp => new ShortcutMapProvider(
                sp.GetRequiredService<ApplicationSettings>().Shortcuts.ToMap()))
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<GsudoElevationLauncher>()
            .AddSingleton<SettingsViewModel>();
    }
```

Replace with:

```csharp
    private static IServiceCollection AddSettingsGraph(this IServiceCollection services)
    {
        return services
            // Single shared instance: the settings page mutates this object and calls Save(), so a
            // second Load() elsewhere would silently diverge from what the user sees.
            .AddSingleton<ApplicationSettings>(_ => ApplicationSettings.Load())
            // The live keymap. Seeded from settings.json so the very first hotkey registration
            // already uses the user's bindings; the settings page pushes replacements on save.
            .AddSingleton<IShortcutMapProvider>(sp => new ShortcutMapProvider(
                sp.GetRequiredService<ApplicationSettings>().Shortcuts.ToMap()))
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<GsudoElevationLauncher>()
            .AddSingleton<SettingsViewModel>()
            // Transient, matching the WPF original's own SettingsWindowViewCoordinator, which
            // explicitly sets ReuseInstances = false: a fresh window each time, closed (not
            // reused) after use, unlike the switcher's singleton reuse.
            .AddTransient<Views.SettingsWindow>()
            // Singleton, rooted explicitly in App.xaml.cs's OnLaunched, same reasoning as every
            // other coordinator: its subscription to SettingsViewModel must stay alive for the
            // app's lifetime.
            .AddSingleton<Coordinators.SettingsWindowCoordinator>();
    }
```

- [ ] **Step 3: Wire both into `App.xaml.cs`, remove the now-unused `_window` field**

In `winui3/WinTabberUI/App.xaml.cs`, find:

```csharp
    private Window? _window;

    // Rooted explicitly, not left to whatever gets resolved transitively through _window's own
    // constructor chain -- see AddThumbnailWindowGraph's doc comment. WinTabberEventManager is
    // resolved here for the same reason even though SettingsWindow's own SettingsViewModel dependency
    // already resolves it today: that's an incidental path, not a guarantee, and this app's global
    // hotkey pipeline (InputListenerService, wired from WinTabberEventManager's own constructor)
    // must not depend on which window happens to be shown first.
    private WinTabberEventManager? _eventManager;
    private ThumbnailWindowCoordinator? _thumbnailWindowCoordinator;
    private MediaControlsWindowCoordinator? _mediaControlsWindowCoordinator;
    private NotifyIconCoordinator? _notifyIconCoordinator;
```

Replace with (the `_window` field is removed -- each coordinator now manages its own window instance internally, so the App-level field has no remaining purpose; the doc comment on `_eventManager` is kept since it still explains a real, current design decision, just no longer next to a field it doesn't describe):

```csharp
    // Rooted explicitly, not left to whatever gets resolved transitively through some window's own
    // constructor chain -- see AddThumbnailWindowGraph's doc comment. WinTabberEventManager is
    // resolved here for the same reason even though SettingsWindow's own SettingsViewModel dependency
    // already resolves it today: that's an incidental path, not a guarantee, and this app's global
    // hotkey pipeline (InputListenerService, wired from WinTabberEventManager's own constructor)
    // must not depend on which window happens to be shown first.
    private WinTabberEventManager? _eventManager;
    private ThumbnailWindowCoordinator? _thumbnailWindowCoordinator;
    private MediaControlsWindowCoordinator? _mediaControlsWindowCoordinator;
    private NotifyIconCoordinator? _notifyIconCoordinator;
    private WindowSelectorWindowCoordinator? _windowSelectorWindowCoordinator;
    private SettingsWindowCoordinator? _settingsWindowCoordinator;
```

Then find:

```csharp
        _eventManager = Services.GetRequiredService<WinTabberEventManager>();
        _thumbnailWindowCoordinator = Services.GetRequiredService<ThumbnailWindowCoordinator>().Init();
        _mediaControlsWindowCoordinator = Services.GetRequiredService<MediaControlsWindowCoordinator>();
        _notifyIconCoordinator = Services.GetRequiredService<NotifyIconCoordinator>();
```

Replace with:

```csharp
        _eventManager = Services.GetRequiredService<WinTabberEventManager>();
        _thumbnailWindowCoordinator = Services.GetRequiredService<ThumbnailWindowCoordinator>().Init();
        _mediaControlsWindowCoordinator = Services.GetRequiredService<MediaControlsWindowCoordinator>();
        _notifyIconCoordinator = Services.GetRequiredService<NotifyIconCoordinator>();
        _windowSelectorWindowCoordinator = Services.GetRequiredService<WindowSelectorWindowCoordinator>();
        _settingsWindowCoordinator = Services.GetRequiredService<SettingsWindowCoordinator>();
```

Finally, find the trailing comment block (now stale -- both coordinators it says don't exist, now do):

```csharp
        // No window is shown at launch: the app now starts quietly in the tray. SettingsWindow
        // and WindowSelectorWindow are shown on demand once their coordinators exist (a following,
        // separate task) -- until then there is intentionally no way to open a window from the
        // running app, per this plan's explicit startup-lifecycle scope change.
    }
```

Replace with:

```csharp
        // No window is shown at launch: the app starts quietly in the tray. SettingsWindow and
        // WindowSelectorWindow are now shown on demand, driven by WindowSelectorWindowCoordinator/
        // SettingsWindowCoordinator reacting to their view models' own IObservable<bool> signals.
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Manual verification**

Kill any running instance (`taskkill //IM WinTabberUI.exe //F`), rebuild, launch
`winui3\WinTabberUI\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\WinTabberUI.exe`.
Confirm: right-click the tray icon, click "Show Window" — the window selector appears and takes
focus. Left-click the tray icon — same result. Trigger whatever hides the switcher today (its own
`CmdAppHide`-driven behavior, already working, unmodified) and confirm it hides. Right-click the
tray icon, click "Settings" — the settings window appears and takes focus. Close it via its own
titlebar close button, then click "Settings" again from the tray menu — confirm it reopens
correctly (this is the externally-closed fix; if it fails to reopen, `IsSettingsShown` got stuck).

- [ ] **Step 6: Commit**

```bash
git add winui3/WinTabberUI/Bootstrapper.cs winui3/WinTabberUI/App.xaml.cs
git commit -m "feat: wire window-selector and settings coordinators into winui3 startup"
```

---

### Task 4: Update the migration progress ledger

**Files:**
- Modify: `.superpowers/sdd/2026-09-12-wpf-to-winui3-migration/progress.md` (gitignored; not committed)

- [ ] **Step 1: Append an entry**

Add an entry noting: `WindowSelectorWindow` and `SettingsWindow` are now reachable from the tray
icon's "Show Window"/"Settings" menu items and left-click, via two new coordinators
(`WindowSelectorWindowCoordinator`, `SettingsWindowCoordinator`) following the established
`MediaControlsWindowCoordinator` shape; `WindowSelectorWindow`'s DI lifetime changed from
transient to singleton to support reuse, matching the WPF original; `SettingsWindow` is now
registered in DI for the first time; the tray icon's previously-inert menu items are now fully
functional except "Media debug view" (still excluded, `MediaDebugWindow` unported) and "Enable
Hooks" (separately tracked, deferred click-routing bug).

- [ ] **Step 2: No commit** (ledger is gitignored).

---

## Self-Review

**Spec coverage:**
- `WindowSelectorWindowCoordinator` (singleton reuse) → Task 1.
- `SettingsWindowCoordinator` (transient, externally-closed fix) → Task 2.
- DI lifetime ruling (`WindowSelectorWindow` transient → singleton) and new `SettingsWindow`
  registration → Task 3.
- `App.xaml.cs` wiring and `_window` field removal → Task 3.
- Explicit exclusions (`ViewCoordinatorBase<T>`, view-model changes, Enable Hooks bug,
  `MediaDebugWindow`) → nothing in this plan touches or references them.

**Placeholder scan:** No TBD/TODO markers. Every step has literal code; `WindowEventArgs` was
confirmed against the SDK's own metadata before writing this plan, not left as an open question.

**Type consistency:** `WindowSelectorWindowCoordinator(WindowSelectorViewModel, IServiceProvider, IWindowInterop)`
and `SettingsWindowCoordinator(SettingsViewModel, IServiceProvider, IWindowInterop)` are each
defined once (Tasks 1/2) and registered/resolved with those exact types in Task 3.
