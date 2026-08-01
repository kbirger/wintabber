# Process Suspension in WinTabber — Implementation Plan

Ports the behavior of the standalone `ProcessSuspender` app (`C:\Users\kir\git\ProcessSuspender`)
into WinTabber, and adds a second, non-focusable "suspended windows" bar that appears alongside
the window selector.

## Goals

1. A **sleep button** in the tab-title component (`EditableTextBlock`) of `WindowSelectorWindow`
   that hides the window and suspends (freezes) its process, exactly as `ProcessSuspender` does.
2. A **second window** (`SuspendedWindowsWindow`), triggered by the same event that shows
   `WindowSelectorWindow`, that pops up at the **bottom of the screen** whenever any suspended
   processes are known.
   - Always above plain (non-topmost) windows.
   - **Never** takes focus from `WindowSelectorWindow`, regardless of show ordering.
   - Clicking any item resumes that process and hides **both** windows.
   - Selecting a window in `WindowSelectorWindow` hides **both** windows.

---

## 1. Interop layer (`WinTabber.Interop`)

Per CLAUDE.md, no P/Invoke outside this project. Add to `IInteropProxy` / `InteropProxy`:

| Member | Notes |
| --- | --- |
| `void SuspendProcess(int pid)` | `NtSuspendProcess` |
| `void ResumeProcess(int pid)` | `NtResumeProcess` |
| `void HideWindow(int handle)` | `ShowWindow(SW_HIDE)` |
| `void RestoreWindow(int handle)` | `ShowWindow(SW_RESTORE)` |
| `string GetProcessImagePath(int pid)` | `QueryFullProcessImageName` (already in `NativeMethods.txt`) |
| `void EnableDebugPrivilege()` | `OpenProcessToken` + `LookupPrivilegeValue` + `AdjustTokenPrivileges` |

- `NtSuspendProcess` / `NtResumeProcess` are undocumented and **not** in CsWin32 metadata → hand-written
  `[DllImport("ntdll.dll")]` in a `NtNativeMethods.cs` inside `WinTabber.Interop`. Copy from
  `ProcessSuspender/NativeMethods.cs`.
- Add `ShowWindow`, `LookupPrivilegeValue`, `AdjustTokenPrivileges`, `TOKEN_PRIVILEGES` to
  `WinTabber.Interop/NativeMethods.txt` (`OpenProcess`, `OpenProcessToken`, `CloseHandle`,
  `QueryFullProcessImageName` are already listed).
- `EnableDebugPrivilege()` is called **once at startup** (`BackgroundServiceContainer` ctor), not from a
  manager constructor as in `ProcessSuspender`.

## 2. Domain layer (`WinTabber.API/Suspension/`)

Ported from `ProcessSuspender`, adapted to WinTabber's DI + reactive idiom.

- `ISuspensionStrategy`, `NtProcessSuspensionStrategy`, `ThreadSuspensionStrategy` — direct ports; the
  P/Invoke bodies are replaced by `IInteropProxy` calls.
- `SuspendedWindowEntry` — extended record:
  ```csharp
  public sealed record SuspendedWindowEntry(
      int ProcessId,
      IReadOnlyList<long> WindowHandles,  // was: single MainWindowHandle
      string PathHash,
      string ProcessName,                 // for the bar's display
      string Title,                       // for the bar's display
      string StrategyName = "process");
  ```
  **Why the handle list:** suspending freezes *every* window of the process, but the button is
  per-`WindowRef`. Capture all currently visible top-level handles via
  `windowRef.Process.GetWindows()` *before* hiding, and hide/restore all of them — otherwise the
  process's other windows stay visible but frozen-painted. `ProcessName`/`Title` are captured at
  suspend time because they cannot be read reliably off a suspended, hidden process.
- `SuspendedWindowState` + `SuspendedWindowStateFile` — direct ports. Persist to
  `Paths.SuspensionStateFilePath` (new entry in `WinTabberUI/Paths.cs`, under `RoamingDataPath`).
- `IProcessSuspensionService` / `ProcessSuspensionService` — the WinTabber-facing API:
  - `bool CanSuspend(WindowRef window)` — false if `window.Process.IsProcessElevated`, if the PID is
    WinTabber's own (`IProcessRepository.GetCurrentProcessId()`), or if already suspended.
  - `bool Suspend(WindowRef window)` / `bool Resume(int pid)` / `void ResumeAll()`.
  - Reactive state via DynamicData: `SourceCache<SuspendedWindowEntry,int>` exposed as
    `IObservable<IChangeSet<SuspendedWindowEntry,int>> Connect()` and
    `IObservable<bool> HasSuspendedChanges` (`.CountChanged().Select(c => c > 0).DistinctUntilChanged().StartWith(...)`).
  - **Startup pruning:** on load, re-verify each persisted entry's path hash against the live PID
    (same check `ProcessSuspender.DoResume` does at resume time) and drop stale entries. Without this,
    the bar shows dead/PID-reused entries and `HasSuspendedChanges` is wrong from the first frame.
  - Suspend/resume failures are swallowed + logged (never crash the switcher); on resume failure the
    entry is still removed so the user isn't stuck with an unresumable row.

## 3. Suspend button in `EditableTextBlock`

**ViewModel** (`WindowItem`):
- `ReactiveCommand<Unit,Unit> SuspendCommand`, `canExecute` = `suspensionService.CanSuspend(WindowRef)`
  combined with `canEdit`-style gating (disabled while editing).
- `WindowItem`'s constructor gains an `IProcessSuspensionService` parameter; the construction site is
  `WindowSelectorViewModel.Update()` (`WindowSelectorViewModel.cs:185`).

**View** (`Views/EditableTextBlock.xaml`): a third `Button` in the existing `StackPanel` (Grid.Column 1),
`Style="{StaticResource InlineButton}"`, `<fa:IconBlock Icon="Moon"/>`, visible when **not** editing
(needs an inverse of `BoolToVisibilityConverter` — add to `WinTabber.UI.Common.ValueConverters` or reuse
`InverseBoolConverter` + `BoolToVisibilityConverter` via a chain converter).

**Click plumbing — the trap.** `EditableTextBlock` routes `PreviewMouseDown` from the root,
`BorderContainer`, *and* `TextBox` into `BorderContainer_MouseDown` (`EditableTextBlock.xaml.cs:17-20`),
which enters edit mode; and the item template's `Grid` has `MouseUp="Grid_MouseUp"`
(`WindowSelectorWindow.xaml:51`) → `SwitchWindowAndClose()`. A naive button click therefore both enters
edit mode and switches away. Fix on **both** sides:
- In `BorderContainer_MouseDown`, bail out when `e.OriginalSource` sits under a `Button` (walk the
  visual tree with `VisualTreeHelper.GetParent`).
- In `Grid_MouseUp`, ignore events whose `OriginalSource` sits under an `EditableTextBlock`. Do **not**
  rely on `ButtonBase` swallowing the bubble — WPF's `MouseUp`/`MouseLeftButtonUp` promotion makes that
  unreliable. This also fixes the pre-existing latent bug of clicking Accept/Cancel closing the switcher.

**List refresh after suspend — the second trap.** Do **not** call `WindowSelectorViewModel.Update()`:
its `WindowItems` setter disposes every existing item (`WindowSelectorViewModel.cs:116`) and it resets
`SelectedIndex = -1` (`:184`), destroying the user's Alt-tab position and tearing down the very
`WindowItem` whose button was just clicked. Instead:
- Preferred: add `bool IsSuspended` to `WindowItem`, driven off the suspension service, and gray the
  tile out in place (the `WindowThumbnail` bound to a now-hidden `Handle` renders blank anyway).
- Alternative: remove that single item from the `WindowItems` array while preserving `SelectedIndex`.

Also verify during implementation: because the selector holds focus when the button is clicked, the
hidden window is *not* the foreground window, so `ActiveWindowChangedEvents` should not fire. If it
does, `WindowSelectorViewModel`'s `winChanges` subscription (`:46`) hits the same `Update()` teardown,
and a null `ActiveApplicationChanges` would hit `Clear` → `Deactivate()` and close the switcher outright.

## 4. `SuspendedWindowsWindow` (the bottom bar)

**View** — `WinTabberUI/Views/SuspendedWindowsWindow.xaml`, modeled on `MediaControlsWindow`:
```
WindowStyle="None" AllowsTransparency="True" ShowInTaskbar="False"
Topmost="True" ShowActivated="False" Focusable="False" SizeToContent="WidthAndHeight"
```
plus `AcrylicChrome` for visual consistency with the selector.

**Never-focused guarantee.** In `OnSourceInitialized`, OR `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` into
the window's `GWL_EXSTYLE`. This is the whole answer to the "tricky because of the event-driven nature"
concern: rather than sequencing show/activate calls between two coordinators, make the window
*incapable* of activation at the Win32 level. Clicks still hit its buttons; focus stays on
`WindowSelectorWindow`. `ShowActivated="False"` alone is **not** enough — a click would activate it.
Consequently **no** activation-ordering logic is needed anywhere, and the window must **not** be given
`Owner = windowSelector` (owner-based z-order would follow activation).

**Positioning** — bottom-center of `WindowSelectorViewModel.CursorScreen.WorkingArea` (working area, not
`Bounds`, so it clears the taskbar), converted with the `DpiHelper.DeviceRectToLogical` pattern already
used in `WindowSelectorWindow.CenterWindow()` (`WindowSelectorWindow.xaml.cs:215`). Recompute on
`SizeChanged` / show.

**ViewModel** — `SuspendedWindowsViewModel`:
- `ReadOnlyObservableCollection<SuspendedWindowItemViewModel> Items`, bound off
  `suspensionService.Connect().ObserveOn(RxApp.MainThreadScheduler).Bind(out …).Subscribe()`.
- `SuspendedWindowItemViewModel` exposes `ProcessName`, `Title`, icon, and
  `ResumeCommand` → `suspensionService.Resume(pid)` then
  `eventManager.SendEvent(EventType.WindowSelected)`.

**Hiding both windows — one mechanism.** `IsSwitcherActiveChanges` already maps
`EventType.WindowSelected => false` (`WindowSelectorViewModel.cs:97`). So:
- Clicking a bar item → `SendEvent(WindowSelected)` → selector hides **and** the bar's own
  visibility observable goes false.
- Selecting a window in the selector → `SelectAndClose()` already sends `WindowSelected`
  (`WindowSelectorViewModel.cs:163`) → both hide. **No extra work needed.**
- Releasing Alt (`CmdAppHide`, when not editing) → both hide, same path.

## 5. Coordinator + DI wiring

`WinTabberUI/Coordinators/SuspendedWindowsViewCoordinator.cs`, mirroring
`WindowSelectorViewCoordinator`:
```csharp
ReuseInstances = true;
protected override IObservable<bool> GetChangeEvents() =>
    _selectorVm.IsSwitcherActiveChanges
        .CombineLatest(_suspensionService.HasSuspendedChanges, (active, has) => active && has)
        .DistinctUntilChanged();
protected override void Show(SuspendedWindowsWindow i) => i.Show();  // ShowActivated=False
protected override void Close(SuspendedWindowsWindow i) => i.Hide();
```
This also satisfies "pop up only when any suspended processes are known", and auto-hides the bar the
moment the last process is resumed.

`Bootstrapper.cs`:
- `AddDomainModels`: `.AddSingleton<IProcessSuspensionService, ProcessSuspensionService>()`,
  `.AddSingleton<ISuspensionStrategy[]>(…)` (or register the two strategies + an array factory).
- `AddCoordinators`: `.AddSingleton<SuspendedWindowsViewCoordinator>()`.
- `AddViewModels`: `.AddSingleton<SuspendedWindowsViewModel>()`.
- `AddViews`: `.AddTransient<SuspendedWindowsWindow>()` (or a singleton factory like the selector).

`BackgroundServiceContainer.cs` — **easy to omit, and the feature is dead without it**: add
`ioc.GetRequiredService<SuspendedWindowsViewCoordinator>().Init()` to the `CompositeDisposable`, and
call `EnableDebugPrivilege()` in the ctor.

## 6. Lifecycle safety

The worst failure mode is a process left frozen *and* hidden with no UI to recover it.

- **Resume-all on exit.** `App.OnExit` disposes `BackgroundServiceContainer` (`App.xaml.cs:43-46`), so
  `ProcessSuspensionService.Dispose()` → `ResumeAll()` covers normal shutdown. Also resume-all from the
  tray-icon Exit path (`NotifyIconViewModel`) for the same reason.
- **Crash / kill recovery** is what the persisted state file plus startup pruning (§2) is for: on next
  launch the bar repopulates with still-valid entries so the user can resume them. Entries whose PID is
  gone or whose path hash no longer matches are dropped.
- **Tray menu escape hatch:** add a "Resume all suspended" item to `NotifyIconViewModel`, enabled off
  `HasSuspendedChanges`, so recovery never depends on the switcher being reachable.

## 7. Implementation order

1. Interop members + `NativeMethods.txt` entries + ntdll `DllImport`s.
2. `WinTabber.API/Suspension/` port (strategies, entry, state, state file) — no UI.
3. `ProcessSuspensionService` + persistence + startup pruning + DI registration + tray "Resume all".
4. `WindowItem.SuspendCommand` + `EditableTextBlock` button + both click-routing guards.
5. In-place `IsSuspended` handling in the selector list.
6. `SuspendedWindowsWindow` + ViewModel + `WS_EX_NOACTIVATE` + bottom positioning.
7. Coordinator + `BackgroundServiceContainer` wiring.
8. Manual verification (below).

## Implementation status (2026-08-01)

All eight steps are implemented and the solution builds clean; `WinTabber.Api.Tests` is green at 18/18.

Verified automatically:
- `ProcessSuspensionService` and `SuspendedWindowFileStore` under unit test (suspend/hide/persist, all four
  `CanSuspend` refusals, rollback when the strategy throws, abort-before-hide when the image path can't be
  resolved, resume via the entry's recorded strategy, reused-PID hash mismatch, `ResumeAll`, startup pruning,
  `HasSuspendedChanges` lifecycle + late subscriber, file round-trip / missing / corrupt / delete).
- The app launches and stays alive, which exercises the whole DI graph eagerly through
  `BackgroundServiceContainer` — a missing registration for the new service, view model, window, or
  coordinator would throw at `OnStartup`. `%APPDATA%\WinTabber\Suspension\suspended_state.json` is created
  and written on first run, confirming the directory-creation and startup-prune paths execute.

**Not yet verified — §8 below is unexecuted.** The machine was locked when verification was attempted, so no
GUI interaction was possible. The runtime-only risks are:
1. Whether the bar's tile buttons receive clicks at all under `WS_EX_NOACTIVATE` + `Focusable="False"`.
2. Whether `AcrylicChrome` overwrites `GWL_EXSTYLE` after `OnSourceInitialized` and drops `WS_EX_NOACTIVATE`
   — it writes that same field for `WS_EX_LAYERED`. Symptom: clicking the bar steals focus and collapses the
   selector.
3. Per-monitor DPI: `PositionWindow()` reads DPI via `VisualTreeHelper.GetDpi(this)`, which on first show may
   not reflect the target monitor. `SuspendedWindowsWindow` does not handle `DpiChanged` the way
   `WindowSelectorWindow` does.

## 8. Manual verification checklist

- Alt+` opens selector; suspend a Notepad window → it vanishes, bar appears at the bottom, **focus
  stays on the selector** (arrow keys / Alt+` still cycle tiles).
- Click the taskbar/another app while the bar is up → bar does not activate.
- Click a bar item → process resumes, its window reappears and comes to the front, **both** windows hide.
- Select a window in the selector (Alt release or click) → both windows hide; bar reappears on the next
  Alt+` because entries remain.
- Resume the last entry → bar hides immediately.
- Elevated-process window → suspend button disabled, no exception.
- Kill WinTabber with a process suspended, relaunch → entry survives and is resumable; kill the
  suspended process instead, relaunch → entry is pruned.
- Exit via tray → all suspended processes resume.
