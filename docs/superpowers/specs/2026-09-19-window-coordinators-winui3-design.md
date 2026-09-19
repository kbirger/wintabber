# Window coordinators: WinUI 3 port design

Date: 2026-09-19
Status: proposed (designed unattended per explicit user authorization; see
Rulings section — no human Q&A occurred during this design pass)

## Purpose

The tray icon (Phase 5, sub-project 1) sends `CmdNextWindow`/`CmdShowSettings`
events through `WinTabberEventManager` when "Show Window"/"Settings" are
clicked, or the tray icon is left-clicked. Nothing in winui3 subscribes to
either event yet, so both are currently inert. This is the second Phase 5
sub-project: wire up coordinators so `WindowSelectorWindow` and
`SettingsWindow` actually appear in response to these events, matching what
the WPF app already does. The user's explicit goal for this sub-project is
to be able to call up the window selector from the running winui3 app.

## Key finding: no new design needed for the hard part

Both target windows' view models already expose a ready-made, framework-free
"should this window be visible right now" signal, already reacting to the
right event, already shared and unmodified:

- `WindowSelectorViewModel.IsSwitcherActiveChanges` (`WinTabber.ViewModels/WindowSelectorViewModel.cs:107`)
  — an `IObservable<bool>`, already driven by `CmdNextWindow`/`CmdPreviousWindow`/
  `CmdAppHide`/`CmdCommitSelection`/`WindowSelected` events.
- `SettingsViewModel.IsSettingsShown` (`WinTabber.ViewModels/SettingsWindowViewModel.cs:86`
  — file name doesn't match the class name, `SettingsViewModel`, a
  pre-existing naming quirk, not touched by this design) — an
  `IObservable<bool>`, already driven by `CmdShowSettings`, with a
  `Hide()` method already available for the coordinator to call back.

Neither view model needs any change. This is not new design work — it is
wiring two thin coordinators onto signals that already exist, using the
exact shape `ThumbnailWindowCoordinator`/`MediaControlsWindowCoordinator`
already established in winui3 (a plain class subscribing to an
`IObservable<bool>`, no `ViewCoordinatorBase<T>` port — that WPF base class
was explicitly not ported, per `MediaControlsWindowCoordinator`'s own doc
comment, since every winui3 coordinator so far uses this simpler shape for
a single consumer).

## Two windows, two different lifetimes — a real design decision, not copy-paste

**`WindowSelectorWindow`: singleton, reused, matching `MediaControlsWindowCoordinator`'s pattern.**
The WPF original's `WindowSelectorViewCoordinator` explicitly sets
`ReuseInstances = true` — this is a single global switcher, shown/hidden
repeatedly, never rebuilt per show. **Ruling:** winui3's current
`Bootstrapper.cs` registers `Views.WindowSelectorWindow` as
`AddTransient` (`AddWindowSelectorGraph`, line 153) — this is wrong for a
reuse-based coordinator and must change to `AddSingleton`. This was set
transient before any coordinator existed to show it, likely just following
the neighboring `ThumbnailWindow`'s registration by proximity, not a
deliberate lifetime decision — no coordinator existed yet to reveal the
mismatch. Cost if this ruling is wrong: a rebuilt window on every show
instead of a reused one, which would still function, just slower and
without preserved window-list state across shows — no correctness risk
either way, since nothing currently depends on `WindowSelectorWindow`
being transient.

**`SettingsWindow`: transient, fresh instance per show, matching the WPF original.**
The WPF original's `SettingsWindowViewCoordinator` sets
`ReuseInstances = false` — a fresh window each time, closed (not just
hidden) after use. `SettingsWindow` is not currently registered in
winui3's DI container at all (it was previously constructed directly,
once, in the now-removed `OnLaunched` line). **Ruling:** register it
`AddTransient`, matching the WPF original's lifetime choice and the
existing `ThumbnailWindow`/`WindowSelectorWindow`-before-this-ruling
precedent for windows that get rebuilt per show.

## Coordinators

Two new files, each following `MediaControlsWindowCoordinator`'s exact
shape:

```csharp
// winui3/WinTabberUI/Coordinators/WindowSelectorWindowCoordinator.cs
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
                if (isActive) ShowWindow();
                else _window?.Hide();
            });
    }

    private void ShowWindow()
    {
        _window ??= _serviceProvider.GetRequiredService<WindowSelectorWindow>();
        _window.Show();

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (handle == nint.Zero) return;
        _interop.ForceForeground((int)handle);
    }

    public void Dispose() => _subscription.Dispose();
}
```

`SettingsWindowCoordinator` follows the identical shape, subscribing to
`SettingsViewModel.IsSettingsShown` instead, with one addition matching the
WPF original's own correctness fix: since `SettingsWindow` is user-closable
via its own titlebar (unlike the switcher, which the WPF app never lets the
user close directly), the coordinator must call `vm.Hide()` when the window
is closed externally, or `IsSettingsShown` would stay stuck `true` after
the user closes the window by any means other than the tray menu:

```csharp
private void ShowWindow()
{
    _window = _serviceProvider.GetRequiredService<SettingsWindow>();
    _window.Closed += OnWindowClosed;
    _window.Show();
    // ForceForeground, same as above
}

private void OnWindowClosed(object sender, WindowEventArgs args)
{
    _window!.Closed -= OnWindowClosed;
    _window = null;
    _vm.Hide();
}
```

`ForceForeground` is needed on both, for the same reason
`MediaControlsWindowCoordinator` documents: the hotkey/tray-click arrives
through a global hook, so the process holds no foreground right, and
`Show()` alone would not bring the window to the front.

## DI wiring

- `winui3/WinTabberUI/Bootstrapper.cs`'s `AddWindowSelectorGraph`: change
  `Views.WindowSelectorWindow` from `AddTransient` to `AddSingleton`
  (ruling above), add
  `.AddSingleton<Coordinators.WindowSelectorWindowCoordinator>()`.
- Add `Views.SettingsWindow` as `AddTransient` and
  `Coordinators.SettingsWindowCoordinator` as `AddSingleton` to
  `AddSettingsGraph`.
- `App.xaml.cs`'s `OnLaunched`: resolve both coordinators eagerly, the same
  pattern as `ThumbnailWindowCoordinator`/`MediaControlsWindowCoordinator`.
  This also finally gives the now-unused `_window` field and its
  `SettingsWindow`/`SettingsViewModel` usings (left behind, with a warning,
  by the tray-icon plan's own startup-lifecycle change) a real purpose
  again — though the coordinator resolves its own window instance
  internally rather than reusing that field, so the field can likely be
  removed at this point rather than reused verbatim. This will be decided
  concretely in the implementation plan once the exact code is in view,
  not asserted here.

## Explicit exclusions

- `ViewCoordinatorBase<T>` — not ported, per established winui3 precedent.
- Any change to `WindowSelectorViewModel`/`SettingsViewModel` — both are
  already correct and shared as-is.
- Any fix to the still-deferred "Enable Hooks" click-routing bug — unrelated,
  tracked separately in the migration plan doc.
- `MediaDebugWindow` reachability — still not investigated, out of scope here.

## Testing

No unit tests: this is DI/UI wiring with no new logic (`WindowSelectorViewModel`/
`SettingsViewModel` are unmodified). Verification is manual: launch the app,
use the tray icon's "Show Window" (and left-click) to confirm the window
selector appears and takes focus; press its hide key or trigger `CmdAppHide`'s
usual mechanism to confirm it hides; use "Settings" to confirm the settings
window appears; close it via its own titlebar and re-open via the tray menu
to confirm the externally-closed fix works (no stuck-open state).

## Rulings made during this unattended design pass

Per explicit user authorization to continue without further check-ins:

1. **`WindowSelectorWindow`'s DI lifetime changes from transient to
   singleton.** Reasoning and cost above. This is the one ruling with any
   real design weight — everything else in this spec is a direct,
   low-judgment port of an existing, working WPF pattern onto
   already-shared view-model signals.
2. **`SettingsWindow`'s closed-externally handling is ported faithfully**
   (calling `vm.Hide()` on external close), rather than omitted as
   out-of-scope, because skipping it would introduce a real, easily-missed
   state-desync bug into new code where the WPF original deliberately
   avoids it.
3. **Both coordinators use the simple established winui3 shape**
   (`ThumbnailWindowCoordinator`/`MediaControlsWindowCoordinator`'s
   pattern), not a `ViewCoordinatorBase<T>` port — this was already decided
   by precedent before this design pass, not a new call.
