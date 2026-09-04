# Phase 5A: Interop Layer Split & DI Ordering — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the 39-member `IInteropProxy` into four consumer-driven interfaces, fold in the
related dead-code/CsWin32/dedup fixes discovered while planning, and make
`BackgroundServiceContainer`'s two implicit ordering requirements structural instead of
comment-enforced.

**Architecture:** `InteropProxy` (unchanged internals) implements `IProcessControl`,
`IWindowPlacement`, and `IWindowInterop` (which itself extends `IWindowVisibility`). Consumers
narrow their constructor dependencies to only the interface(s) they call. DI registers the one
`InteropProxy` singleton and exposes each interface as a thin factory over it.

**Tech Stack:** C# / .NET 10, WPF, Microsoft.Extensions.DependencyInjection, CsWin32, TUnit.

**Spec:** `docs/superpowers/specs/2026-09-04-phase-5-design.md` (T5.1 and T5.2 sections; read
both before starting — this plan implements them and assumes their reasoning).

## Global Constraints

- Every task must end with `dotnet build WinTabber.slnx` at 0 warnings, 0 errors, and
  `dotnet test --solution WinTabber.slnx` fully green (baseline: 81 passed, 0 failed — confirm
  this hasn't drifted before Task 1).
- Prefer Serena's symbolic tools (`rename_symbol`, `find_referencing_symbols`,
  `safe_delete_symbol`, `replace_symbol_body`) over hand-editing when renaming, deleting live
  code, or checking whether something is used. `rename_symbol` cannot rename a `namespace`
  declaration — not needed in this plan, but noted per `.cleanup/HANDOFF.md`.
- Do not add a `NativeMethods.txt` entry without an actual call site — CsWin32 generates
  dependent types transitively.
- Commit after each task. Follow the existing commit style: `chore: Phase 5A T<n> — <summary>`.
- Do not run `--no-verify` or skip hooks.

---

### Task 1: Split `IInteropProxy` into four interfaces

**Files:**
- Delete: `WinTabber.Interop/IInteropProxy.cs`
- Create: `WinTabber.Interop/IProcessControl.cs`
- Create: `WinTabber.Interop/IWindowVisibility.cs`
- Create: `WinTabber.Interop/IWindowPlacement.cs`
- Create: `WinTabber.Interop/IWindowInterop.cs`
- Modify: `WinTabber.Interop/InteropProxy.cs:18`

**Interfaces:**
- Produces: `IProcessControl`, `IWindowVisibility`, `IWindowPlacement`, `IWindowInterop` (all in
  namespace `WinTabber.Interop`) — the exact member sets every later task depends on.

- [ ] **Step 1: Create `IProcessControl.cs`**

```csharp
using System.Diagnostics;

namespace WinTabber.Interop;

public interface IProcessControl
{
    /// <summary>Suspends all threads of the process atomically (NtSuspendProcess).</summary>
    void SuspendProcess(int pid);

    /// <summary>Resumes all threads of the process atomically (NtResumeProcess).</summary>
    void ResumeProcess(int pid);

    /// <summary>Suspends each thread of the process individually, as PsSuspend does.</summary>
    void SuspendProcessThreads(int pid);

    /// <summary>Resumes each thread of the process individually, as PsSuspend does.</summary>
    void ResumeProcessThreads(int pid);

    /// <summary>Full executable path of the process. Throws InvalidOperationException if it cannot be determined.</summary>
    string GetProcessImagePath(int pid);

    /// <summary>Best-effort enabling of SeDebugPrivilege for the current process. Call once at startup.</summary>
    void EnableDebugPrivilege();
}
```

- [ ] **Step 2: Create `IWindowVisibility.cs`**

```csharp
namespace WinTabber.Interop;

/// <summary>
/// Generic window show/hide operations, split out of <see cref="IWindowInterop"/> so a consumer
/// that only needs visibility (e.g. process suspension, which hides a window while its process is
/// frozen) doesn't have to depend on the full window-interop surface.
/// </summary>
public interface IWindowVisibility
{
    /// <summary>Hides a window (ShowWindow SW_HIDE). No-op if the handle is not a window.</summary>
    void HideWindow(int handle);

    /// <summary>Restores and foregrounds a window (ShowWindow SW_RESTORE + SetForegroundWindow). No-op if the handle is not a window.</summary>
    void RestoreWindow(int handle);
}
```

- [ ] **Step 3: Create `IWindowPlacement.cs`**

```csharp
namespace WinTabber.Interop;

public interface IWindowPlacement
{
    /// <summary>
    /// Captures the window's current placement (via <see cref="IWindowInterop.GetWindowPlacement"/>) and moves it to a
    /// screen-space rectangle guaranteed to be outside every monitor's bounds, keeping its size unchanged.
    /// The window is not hidden (no SW_HIDE/SW_SHOW change) so DWM keeps compositing it and thumbnail
    /// previews keep rendering live. Returns the captured placement so the caller can restore it later.
    /// </summary>
    WindowPlacement MoveWindowOffScreen(int handle);

    /// <summary>
    /// Restores a window to <paramref name="placement"/>'s captured state via SetWindowPlacement (not
    /// SetWindowPos): this re-applies the original showCmd (Normal/Maximized/Minimized) together with
    /// rcNormalPosition in one atomic call, so a window that was maximized when thumbnailed comes back
    /// maximized (on the right monitor) instead of landing as an ordinary window sized to the whole screen.
    /// </summary>
    void RestoreWindowPosition(int handle, WindowPlacement placement);

    /// <summary>
    /// Changes only the window's size (its position, including its off-screen thumbnail position, is left
    /// alone). No-op if the handle is not a window.
    /// </summary>
    void ResizeWindow(int handle, int width, int height);

    /// <summary>
    /// Hides the window's taskbar button (sets WS_EX_TOOLWINDOW, clears WS_EX_APPWINDOW) and returns the
    /// original extended style so it can be restored later via <see cref="RestoreExtendedStyle"/>. Only
    /// call this while the window is positioned off-screen: forcing Explorer to notice the taskbar change
    /// requires a brief hide/show cycle, which would otherwise be a visible flicker. No-op (returns 0) if
    /// the handle is not a window.
    /// </summary>
    int HideFromTaskbar(int handle);

    /// <summary>Restores a previously-captured extended style (see <see cref="HideFromTaskbar"/>). Same off-screen-only caveat applies. No-op if the handle is not a window.</summary>
    void RestoreExtendedStyle(int handle, int originalExStyle);
}
```

- [ ] **Step 4: Create `IWindowInterop.cs`**

```csharp
using System.Diagnostics;

namespace WinTabber.Interop;

public interface IWindowInterop : IWindowVisibility
{
    void BringWindowToFront(int handle);
    IEnumerable<int> EnumerateProcessWindowHandles(Process process);
    void ForceForeground(int hWnd);
    Process? GetForegroundProcess();
    Process? GetWindowProcess(int handle);
    int GetWindowProcessId(int handle);

    string GetWindowTitle(int hWnd);
    void MaximizeWindow(int handle);
    void MinimizeWindow(int handle);

    public int GetForegroundWindowHandle();

    /// <summary>
    /// Activates the live preview
    /// </summary>
    /// <param name="targetWindow">the window to show by making all other windows transparent</param>
    /// <param name="windowToSpare">the window which should not be transparent but is not the target window</param>
    public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare);

    /// <summary>
    /// Deactivates the live preview
    /// </summary>
    public void DeactivateLivePreview();
    WindowPlacement.WindowState GetWindowState(int handle);
    WindowPlacement GetWindowPlacement(int handle);
    void SetWindowText(int handle, string title);
    IObservable<ActiveWindowChangeData> ActiveWindowChangedEvents();
    string GetClassName(int handle);
    void MoveWindow(int handle, Point point);
    bool IsTopLevel(int handle);
    WindowStyles GetWindowStyles(int handle);
    bool IsWindowVisible(int handle);
    bool IsProcessElevated(Process process);
    void SendInput(ushort key, bool down);

    /// <summary>
    /// Sets WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW on the window's extended style so it can never take
    /// focus/activation, even from a mouse click. Clicks still reach its child controls.
    /// </summary>
    void MakeWindowNonActivating(nint handle);

    /// <summary>True if <paramref name="handle"/> still identifies a live window.</summary>
    bool IsWindow(int handle);

    /// <summary>
    /// Asks the window to close (posts WM_CLOSE), the same request a click on its X button or Alt+F4
    /// sends. The window's own message loop decides whether to close immediately, prompt to save, or
    /// ignore the request. No-op if the handle is not a window.
    /// </summary>
    void CloseWindow(int handle);
}
```

Note: `Point` is `System.Drawing.Point`, already resolved in the original file via the same
implicit-usings/global-usings setup — check the original `IInteropProxy.cs` had no explicit
`using System.Drawing;` and relied on `WinTabber.Interop.csproj`'s `ImplicitUsings`/global usings
if the build fails on `Point`; add `using System.Drawing;` to `IWindowInterop.cs` if so.

- [ ] **Step 5: Delete the old interface file**

```bash
rm WinTabber.Interop/IInteropProxy.cs
```

- [ ] **Step 6: Update `InteropProxy`'s declaration**

In `WinTabber.Interop/InteropProxy.cs:18`, change:

```csharp
public class InteropProxy : IInteropProxy
```

to:

```csharp
public class InteropProxy : IProcessControl, IWindowPlacement, IWindowInterop
```

No other change to `InteropProxy.cs` — every member it already implements satisfies one of the
three interfaces (or, for `HideWindow`/`RestoreWindow`, `IWindowVisibility` via `IWindowInterop`).

- [ ] **Step 7: Build**

Run: `dotnet build WinTabber.slnx`

Expected: many `CS0246: The type or namespace name 'IInteropProxy' could not be found` errors
across the solution — this is expected; every consumer is fixed in the following tasks. Confirm
the *only* errors are missing-`IInteropProxy` references (no unrelated errors), then proceed —
do not attempt to make the build pass within this task.

- [ ] **Step 8: Commit**

```bash
git add WinTabber.Interop/
git commit -m "chore: Phase 5A T1 — split IInteropProxy into IProcessControl/IWindowVisibility/IWindowPlacement/IWindowInterop

Solution does not build until Task 2-5 update consumers; this is the interface-only step."
```

---

### Task 2: Update DI registration and narrow `WindowManager`

**Files:**
- Modify: `WinTabberUI/Bootstrapper.cs:83`, `:127`
- Modify: `WinTabber.Api.Windowing/WindowManager.cs:8,14`

**Interfaces:**
- Consumes: `IProcessControl`, `IWindowPlacement`, `IWindowInterop` (Task 1)
- Produces: `WindowManager.Interop` is now typed `IWindowInterop` — every file that reaches
  interop through `Manager.Interop` / `Process.Manager.Interop` (`WindowRef.cs`,
  `WindowProcessRef.cs`, `WindowOwner.cs`, `ApplicationRef.cs`) needs **no changes**, because
  every member they call (`GetForegroundWindowHandle`, `GetForegroundProcess`, `GetWindowProcess`,
  `EnumerateProcessWindowHandles`, `IsProcessElevated`, `IsWindowVisible`, `GetWindowTitle`,
  `GetClassName`, `BringWindowToFront`, `MaximizeWindow`, `MinimizeWindow`, `CloseWindow`,
  `GetWindowState`, `GetWindowPlacement`, `MoveWindow`, `ActivateLivePreview`, `SetWindowText`,
  `IsTopLevel`, `GetWindowStyles`, `DeactivateLivePreview`) is in `IWindowInterop`.

- [ ] **Step 1: Narrow `WindowManager`**

In `WinTabber.Api.Windowing/WindowManager.cs`, change:

```csharp
    public WindowManager(IInteropProxy interop, IProcessRepository processRepository)
    {
        Interop = interop;
        ProcessRepository = processRepository;
    }

    internal IInteropProxy Interop { get; }
```

to:

```csharp
    public WindowManager(IWindowInterop interop, IProcessRepository processRepository)
    {
        Interop = interop;
        ProcessRepository = processRepository;
    }

    internal IWindowInterop Interop { get; }
```

- [ ] **Step 2: Update DI registration in `Bootstrapper.cs`**

In `WinTabberUI/Bootstrapper.cs`, change line 83 from:

```csharp
            .AddSingleton<IInteropProxy, InteropProxy>()
```

to:

```csharp
            .AddSingleton<InteropProxy>()
            .AddSingleton<IProcessControl>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowPlacement>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowInterop>(sp => sp.GetRequiredService<InteropProxy>())
```

This keeps exactly one `InteropProxy` instance for the app's lifetime, shared across all three
interface registrations (`AddSingleton<InteropProxy>()` plus three factories that resolve the
same singleton, rather than three independent `AddSingleton<TInterface, InteropProxy>()` calls,
which would each construct their own instance).

- [ ] **Step 3: Fix the other direct `IInteropProxy` reference in `Bootstrapper.cs`**

At (originally) line 127, inside `AddStateServices`, change:

```csharp
            .AddSingleton<IMediaControlsStateService>(sp => new MediaControlsStateService(
                sp.GetRequiredService<WinTabberEventManager>(),
                sp.GetRequiredService<IInteropProxy>(),
                () => sp.GetRequiredService<ApplicationSettings>().General.EnableMediaControls))
```

to:

```csharp
            .AddSingleton<IMediaControlsStateService>(sp => new MediaControlsStateService(
                sp.GetRequiredService<WinTabberEventManager>(),
                sp.GetRequiredService<IWindowInterop>(),
                () => sp.GetRequiredService<ApplicationSettings>().General.EnableMediaControls))
```

(`MediaControlsStateService`'s own constructor parameter type is fixed in Task 5 — this step
alone will not compile until Task 5 lands; that's expected, same as Task 1.)

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`

Expected: fewer `IInteropProxy`-not-found errors than after Task 1, but still failing — the
Suspension, Thumbnail, and remaining small-consumer files are fixed in Tasks 3-5.

- [ ] **Step 5: Commit**

```bash
git add WinTabberUI/Bootstrapper.cs WinTabber.Api.Windowing/WindowManager.cs
git commit -m "chore: Phase 5A T2 — DI registration + WindowManager narrowed to IWindowInterop"
```

---

### Task 3: Narrow Suspension consumers; shrink the fake

**Files:**
- Modify: `WinTabber.Api.Windowing/Suspension/NtProcessSuspensionStrategy.cs`
- Modify: `WinTabber.Api.Windowing/Suspension/ThreadSuspensionStrategy.cs`
- Modify: `WinTabber.Api.Windowing/Suspension/ProcessSuspensionService.cs`
- Rename+Modify: `WinTabber.Api.Windowing.Tests/Fakes/FakeInteropProxy.cs` → `FakeProcessControl.cs`
- Modify: `WinTabber.Api.Windowing.Tests/Suspension/ProcessSuspensionServiceTests.cs`

**Interfaces:**
- Consumes: `IProcessControl`, `IWindowVisibility` (Task 1)
- Produces: `FakeProcessControl : IProcessControl, IWindowVisibility` — used by Task 11 too if
  any future test needs it (none currently do beyond this file).

- [ ] **Step 1: Narrow the two strategies**

In `WinTabber.Api.Windowing/Suspension/NtProcessSuspensionStrategy.cs`, change:

```csharp
public sealed class NtProcessSuspensionStrategy(IInteropProxy interop) : ISuspensionStrategy
```

to:

```csharp
public sealed class NtProcessSuspensionStrategy(IProcessControl interop) : ISuspensionStrategy
```

In `WinTabber.Api.Windowing/Suspension/ThreadSuspensionStrategy.cs`, change:

```csharp
public sealed class ThreadSuspensionStrategy(IInteropProxy interop) : ISuspensionStrategy
```

to:

```csharp
public sealed class ThreadSuspensionStrategy(IProcessControl interop) : ISuspensionStrategy
```

- [ ] **Step 2: Narrow `ProcessSuspensionService`**

In `WinTabber.Api.Windowing/Suspension/ProcessSuspensionService.cs`, change:

```csharp
    private readonly IInteropProxy _interop;
    private readonly IProcessRepository _processRepository;
    private readonly ISuspendedWindowStore _store;
    private readonly IReadOnlyList<ISuspensionStrategy> _strategies;
    private readonly ISuspensionStrategy _defaultStrategy;
    private readonly SourceCache<SuspendedWindowEntry, int> _cache = new(e => e.ProcessId);

    public ProcessSuspensionService(
        IInteropProxy interop,
        IProcessRepository processRepository,
        ISuspendedWindowStore store,
        IEnumerable<ISuspensionStrategy> strategies
    )
    {
        _interop = interop;
```

to:

```csharp
    private readonly IProcessControl _processControl;
    private readonly IWindowVisibility _windowVisibility;
    private readonly IProcessRepository _processRepository;
    private readonly ISuspendedWindowStore _store;
    private readonly IReadOnlyList<ISuspensionStrategy> _strategies;
    private readonly ISuspensionStrategy _defaultStrategy;
    private readonly SourceCache<SuspendedWindowEntry, int> _cache = new(e => e.ProcessId);

    public ProcessSuspensionService(
        IProcessControl processControl,
        IWindowVisibility windowVisibility,
        IProcessRepository processRepository,
        ISuspendedWindowStore store,
        IEnumerable<ISuspensionStrategy> strategies
    )
    {
        _processControl = processControl;
        _windowVisibility = windowVisibility;
```

Then, still in the same file, replace every remaining `_interop.` call site:
- `_interop.GetProcessImagePath(pid)` (two occurrences, in `Suspend` and `TryGetCurrentPathHash`)
  → `_processControl.GetProcessImagePath(pid)`
- `_interop.HideWindow(handle)` (in `Suspend`) → `_windowVisibility.HideWindow(handle)`
- `_interop.RestoreWindow(handle)` (two occurrences, in `Suspend`'s catch block and `Resume`) →
  `_windowVisibility.RestoreWindow(handle)`

- [ ] **Step 3: Rename and shrink the fake**

Use Serena's `rename_symbol` on the `FakeInteropProxy` class (in
`WinTabber.Api.Windowing.Tests/Fakes/FakeInteropProxy.cs`) to `FakeProcessControl` — this
updates every reference in `ProcessSuspensionServiceTests.cs` automatically. Then rename the
file itself:

```bash
git mv WinTabber.Api.Windowing.Tests/Fakes/FakeInteropProxy.cs WinTabber.Api.Windowing.Tests/Fakes/FakeProcessControl.cs
```

Replace the file's contents with:

```csharp
using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests.Fakes;

/// <summary>
/// Hand-rolled fake for the two interfaces <see cref="Suspension.ProcessSuspensionService"/>
/// depends on. Every member is implemented for real (no <see cref="NotSupportedException"/>
/// stubs) since the suspension domain is exactly what this interface pair was split out to serve.
/// </summary>
public sealed class FakeProcessControl : IProcessControl, IWindowVisibility
{
    public Dictionary<int, string> ImagePaths { get; } = new();
    public HashSet<int> HiddenHandles { get; } = [];
    public List<int> RestoredHandles { get; } = [];
    public List<int> SuspendedProcessPids { get; } = [];
    public List<int> ResumedProcessPids { get; } = [];
    public List<int> SuspendedThreadPids { get; } = [];
    public List<int> ResumedThreadPids { get; } = [];

    public Exception? ThrowOnSuspendProcess { get; set; }
    public Exception? ThrowOnSuspendProcessThreads { get; set; }

    public void SuspendProcess(int pid)
    {
        if (ThrowOnSuspendProcess is not null)
            throw ThrowOnSuspendProcess;
        SuspendedProcessPids.Add(pid);
    }

    public void ResumeProcess(int pid) => ResumedProcessPids.Add(pid);

    public void SuspendProcessThreads(int pid)
    {
        if (ThrowOnSuspendProcessThreads is not null)
            throw ThrowOnSuspendProcessThreads;
        SuspendedThreadPids.Add(pid);
    }

    public void ResumeProcessThreads(int pid) => ResumedThreadPids.Add(pid);

    public void HideWindow(int handle) => HiddenHandles.Add(handle);

    public void RestoreWindow(int handle)
    {
        HiddenHandles.Remove(handle);
        RestoredHandles.Add(handle);
    }

    public string GetProcessImagePath(int pid)
    {
        if (ImagePaths.TryGetValue(pid, out string? path))
            return path;
        throw new InvalidOperationException($"No image path configured for pid {pid}.");
    }

    public void EnableDebugPrivilege() { }
}
```

Note this drops every `NotSupportedException`-throwing member from the old file — there are no
"unused by the suspension domain layer" members left to stub, because the fake now only
implements the two interfaces the suspension domain actually depends on.

- [ ] **Step 4: Fix the test file's two-argument construction sites**

`ProcessSuspensionServiceTests.cs` constructs `new ProcessSuspensionService(interop, ...)` with
a single `interop` argument in two places (`CreateService`'s helper, and
`StartupPruning_DropsStaleEntries_AndPersistsPrunedSet`). Since `FakeProcessControl` implements
both interfaces the constructor now needs, pass it twice:

In the `CreateService` helper method, change:

```csharp
        var service = new ProcessSuspensionService(interop, processRepository, store, [processStrategy, threadStrategy]);
```

to:

```csharp
        var service = new ProcessSuspensionService(interop, interop, processRepository, store, [processStrategy, threadStrategy]);
```

Also update the tuple return type and local variable name from `FakeInteropProxy` to
`FakeProcessControl` if `rename_symbol` in Step 3 did not already rewrite this signature (verify
after the rename — Serena's rename is reference-aware and should have handled it; only touch
this by hand if the build still shows a stale type name here).

In `StartupPruning_DropsStaleEntries_AndPersistsPrunedSet`, change:

```csharp
        var service = new ProcessSuspensionService(
            interop,
            processRepository,
            store,
            [new NtProcessSuspensionStrategy(interop), new ThreadSuspensionStrategy(interop)]
        );
```

to:

```csharp
        var service = new ProcessSuspensionService(
            interop,
            interop,
            processRepository,
            store,
            [new NtProcessSuspensionStrategy(interop), new ThreadSuspensionStrategy(interop)]
        );
```

- [ ] **Step 5: Build and run the Windowing test suite**

Run: `dotnet build WinTabber.slnx`
Expected: still failing elsewhere (Thumbnail/small consumers not yet fixed) — confirm no new
errors in `WinTabber.Api.Windowing`, `WinTabber.Api.Windowing.Tests`, or `WinTabber.Interop`.

Run: `dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj`
Expected: this project alone will not build yet if anything else in the solution it transitively
references is broken — if it builds standalone, all `ProcessSuspensionServiceTests` pass
unchanged (behavior did not change, only the dependency shape).

- [ ] **Step 6: Commit**

```bash
git add WinTabber.Api.Windowing/Suspension/ WinTabber.Api.Windowing.Tests/
git commit -m "chore: Phase 5A T3 — narrow Suspension to IProcessControl/IWindowVisibility, shrink fake to 8 members"
```

---

### Task 4: Narrow `WindowThumbnailService`

**Files:**
- Modify: `WinTabber.Api.Windowing/Thumbnails/WindowThumbnailService.cs`

**Interfaces:**
- Consumes: `IWindowInterop`, `IWindowPlacement` (Task 1)

- [ ] **Step 1: Split the constructor dependency**

Change:

```csharp
    private readonly IInteropProxy _interop;
    private readonly IProcessRepository _processRepository;
    private readonly SourceCache<ThumbnailEntry, int> _cache = new(e => e.Handle);
    private readonly IDisposable _watchdog;

    public WindowThumbnailService(IInteropProxy interop, IProcessRepository processRepository)
    {
        _interop = interop;
        _processRepository = processRepository;
```

to:

```csharp
    private readonly IWindowInterop _windowInterop;
    private readonly IWindowPlacement _windowPlacement;
    private readonly IProcessRepository _processRepository;
    private readonly SourceCache<ThumbnailEntry, int> _cache = new(e => e.Handle);
    private readonly IDisposable _watchdog;

    public WindowThumbnailService(IWindowInterop windowInterop, IWindowPlacement windowPlacement, IProcessRepository processRepository)
    {
        _windowInterop = windowInterop;
        _windowPlacement = windowPlacement;
        _processRepository = processRepository;
```

Then update each call site in the same file:
- `_interop.GetWindowProcessId(handle)` (in `IsOwnWindow`) → `_windowInterop.GetWindowProcessId(handle)`
- `_interop.MoveWindowOffScreen(window.Handle)` (in `StartThumbnail`) → `_windowPlacement.MoveWindowOffScreen(window.Handle)`
- `_interop.HideFromTaskbar(window.Handle)` (in `StartThumbnail`) → `_windowPlacement.HideFromTaskbar(window.Handle)`
- `_interop.ResizeWindow(handle, width, height)` (in `Resize`) → `_windowPlacement.ResizeWindow(handle, width, height)`
- `_interop.RestoreExtendedStyle(handle, lookup.Value.OriginalExStyle)` (in `StopThumbnail`) → `_windowPlacement.RestoreExtendedStyle(handle, lookup.Value.OriginalExStyle)`
- `_interop.RestoreWindowPosition(handle, lookup.Value.Placement)` (in `StopThumbnail`) → `_windowPlacement.RestoreWindowPosition(handle, lookup.Value.Placement)`
- `_interop.BringWindowToFront(handle)` (in `StopThumbnail`) → `_windowInterop.BringWindowToFront(handle)`
- `_interop.IsWindow(entry.Handle)` (in `PruneDestroyedWindows`) → `_windowInterop.IsWindow(entry.Handle)`

Also fix the doc comment on line 30 referencing `IInteropProxy` ("there's no dedicated 'window
destroyed' event flowing through IInteropProxy") — reword to `IWindowInterop` or drop the type
name, since it's describing a general limitation, not naming the exact type on purpose.

- [ ] **Step 2: Fix the DI registration**

`WinTabber.Api.Windowing/Thumbnails/WindowThumbnailService.cs` is registered in
`WinTabberUI/Bootstrapper.cs` as `.AddSingleton<IWindowThumbnailService, WindowThumbnailService>()`
— this uses implicit constructor injection, so no explicit factory needs updating; DI resolves
`IWindowInterop` and `IWindowPlacement` automatically now that Task 2 registered both.

- [ ] **Step 3: Build**

Run: `dotnet build WinTabber.slnx`
Expected: only the small remaining consumers (Task 5) and the chrome relocation (Task 8) still
failing.

- [ ] **Step 4: Commit**

```bash
git add WinTabber.Api.Windowing/Thumbnails/WindowThumbnailService.cs
git commit -m "chore: Phase 5A T4 — narrow WindowThumbnailService to IWindowInterop + IWindowPlacement"
```

---

### Task 5: Narrow the remaining small `IInteropProxy` consumers

**Files:**
- Modify: `WinTabberUI/Coordinators/MediaWindowViewCoordinator.cs`
- Modify: `WinTabberUI/Coordinators/ThumbnailWindowCoordinator.cs`
- Modify: `WinTabber.UI.Media/Services/MediaControlsStateService.cs`
- Modify: `WinTabber.Events/HyperKeyState.cs`
- Modify: `WinTabber.Events/WinTabberEventManager.cs`

**Interfaces:**
- Consumes: `IWindowInterop` (Task 1)

Every one of these five files has the identical shape: a field and constructor parameter typed
`IInteropProxy`, calling only members that live in `IWindowInterop`. Change the type in each —
no other line changes.

- [ ] **Step 1: `MediaWindowViewCoordinator.cs`**

Change:
```csharp
        private readonly IInteropProxy _interop;

        public MediaWindowViewCoordinator(
            ApplicationStateViewModel vm,
            IInteropProxy interop,
            IServiceProvider provider
        )
```
to:
```csharp
        private readonly IWindowInterop _interop;

        public MediaWindowViewCoordinator(
            ApplicationStateViewModel vm,
            IWindowInterop interop,
            IServiceProvider provider
        )
```

(Calls `_interop.GetForegroundWindowHandle()` and `_interop.ForceForeground(...)` — both in
`IWindowInterop` — unchanged.)

- [ ] **Step 2: `ThumbnailWindowCoordinator.cs`**

Change the `IInteropProxy interop` constructor parameter and `private readonly IInteropProxy
_interop;` field to `IWindowInterop`. (Calls `GetForegroundWindowHandle()`, `GetWindowTitle(...)`
— both in `IWindowInterop`.)

- [ ] **Step 3: `MediaControlsStateService.cs`**

Change:
```csharp
public partial class MediaControlsStateService(WinTabberEventManager eventManager, IInteropProxy interop, Func<bool> isFeatureEnabled)
```
and
```csharp
    private readonly IInteropProxy _interop = interop;
```
to `IWindowInterop`. (Calls `_interop.GetWindowProcessId(handle)` — in `IWindowInterop`.)

- [ ] **Step 4: `HyperKeyState.cs`**

Change the primary-constructor parameter `IInteropProxy interop` and field `private readonly
IInteropProxy _interop = interop;` to `IWindowInterop`. (Calls `_interop.SendInput(...)` — in
`IWindowInterop`.)

- [ ] **Step 5: `WinTabberEventManager.cs`**

Change `private IInteropProxy _interop;` and the constructor parameter `IInteropProxy interop`
to `IWindowInterop`. (Calls `_interop.GetWindowProcessId(...)`, `_interop.GetForegroundWindowHandle()`
— both in `IWindowInterop`. Also verify the doc-comment reference at line ~210 mentioning
`_interop.ActiveWindowChangedEvents()` still reads correctly — no change needed, just confirm it
still compiles as a comment.)

- [ ] **Step 6: Build the full solution**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors — every `IInteropProxy` reference in the solution has now been
replaced except the two intentionally-deferred `Ioc.Default.GetRequiredService<IInteropProxy>()`
call sites in `SuspendedWindowsWindow.xaml.cs` and `MediaDebugWindow.xaml.cs` (T6.2 territory —
**fix these too**, since `IInteropProxy` no longer exists: change both to
`Ioc.Default.GetRequiredService<IWindowInterop>()`. This is a minimal compile-fix, not a T6.2
constructor-injection refactor — leave the `Ioc.Default` usage itself alone, only change the
type argument.)

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: 81 passed, 0 failed, 0 skipped (same count as baseline — no test files besides
`ProcessSuspensionServiceTests.cs`, already fixed in Task 3, reference any of these types).

- [ ] **Step 8: Commit**

```bash
git add WinTabberUI/ WinTabber.UI.Media/ WinTabber.Events/
git commit -m "chore: Phase 5A T5 — narrow remaining consumers to IWindowInterop; solution builds clean"
```

---

### Task 6: Fix `DeleteObject` — delete dead code, migrate the live copy to CsWin32

**Files:**
- Modify: `WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs`
- Modify: `WinTabber.Infrastructure/AppCache.cs`
- Modify: `WinTabber.Infrastructure/WinTabber.Infrastructure.csproj`
- Create: `WinTabber.Infrastructure/NativeMethods.txt`

**Interfaces:** None (internal implementation detail, no public surface change).

- [ ] **Step 1: Delete the dead code in `InstalledApplicationRepository.cs`**

Confirm first (this was verified during planning, re-verify before deleting):

```bash
grep -n "Bitmap2BitmapImage" WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs
```

Expected: exactly 3 lines — the method definition (~171) and two commented-out call sites
(~198, ~294). If a live (non-commented) call site now exists, stop and re-plan this task instead
of deleting.

Delete the method and its `DllImport` (originally lines 168-192):

```csharp
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static BitmapSource Bitmap2BitmapImage(Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap(System.Drawing.Color.Red);
        BitmapSource retval;
        // ... (full method body)
    }
```

and delete the two dead comment lines (`//var zz = Bitmap2BitmapImage(bitmap);` and `//var image
= Bitmap2BitmapImage(bitmap);`).

- [ ] **Step 2: Add CsWin32 to `WinTabber.Infrastructure`**

In `WinTabber.Infrastructure/WinTabber.Infrastructure.csproj`, add to the existing
`<ItemGroup>` containing `PackageReference`s:

```xml
        <PackageReference Include="Microsoft.Windows.CsWin32">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
```

and add a new `<ItemGroup>` (matching the pattern in `WinTabber.Interop.csproj` /
`WinTabber.UI.Common.csproj`):

```xml
    <ItemGroup>
        <None Remove="NativeMethods.txt" />
        <AdditionalFiles Include="NativeMethods.txt" />
    </ItemGroup>
```

- [ ] **Step 3: Create `WinTabber.Infrastructure/NativeMethods.txt`**

```
DeleteObject
```

- [ ] **Step 4: Migrate `AppCache.cs`'s `DeleteObject` to CsWin32**

Change:

```csharp
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private BitmapSource Bitmap2BitmapImage(Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        BitmapSource retval;

        try
        {
            retval = Imaging.CreateBitmapSourceFromHBitmap(
                         hBitmap,
                         IntPtr.Zero,
                         Int32Rect.Empty,
                         BitmapSizeOptions.FromEmptyOptions());
            retval.Freeze();
        }
        finally
        {
            DeleteObject(hBitmap);
        }

        return retval;
    }
```

to:

```csharp
    private BitmapSource Bitmap2BitmapImage(Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        BitmapSource retval;

        try
        {
            retval = Imaging.CreateBitmapSourceFromHBitmap(
                         hBitmap,
                         IntPtr.Zero,
                         Int32Rect.Empty,
                         BitmapSizeOptions.FromEmptyOptions());
            retval.Freeze();
        }
        finally
        {
            Windows.Win32.PInvoke.DeleteObject(new Windows.Win32.Graphics.Gdi.HGDIOBJ(hBitmap));
        }

        return retval;
    }
```

CsWin32's generated `DeleteObject` signature takes an `HGDIOBJ`, not a raw `IntPtr` — wrap it as
shown. If the build reports a different generated signature (CsWin32 versions can differ),
adjust to match what the compiler reports; the build is the arbiter here, per CLAUDE.md.

- [ ] **Step 5: Build and test**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors. If `HGDIOBJ` conversion doesn't compile, check the actual
generated signature (`obj/.../NativeMethods.g.cs` under `WinTabber.Infrastructure/obj/`) and
adjust Step 4 accordingly.

Run: `dotnet test --solution WinTabber.slnx`
Expected: 81 passed, 0 failed (no existing test exercises `AppCache.Bitmap2BitmapImage` or
`InstalledApplicationRepository`, so this is a build-verified change, not test-verified — there
is no seam to unit test icon rendering without a real bitmap handle).

- [ ] **Step 6: Commit**

```bash
git add WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs WinTabber.Infrastructure/
git commit -m "chore: Phase 5A T6 — delete dead DeleteObject/Bitmap2BitmapImage, migrate the live copy to CsWin32"
```

---

### Task 7: Migrate `UacHelper` to CsWin32

**Files:**
- Modify: `WinTabber.Interop/UacHelper.cs`

**Interfaces:** None (internal, `UacHelper` is `internal static class`).

`WinTabber.Interop/NativeMethods.txt` already lists `OpenProcessToken`, `GetTokenInformation`,
and `TOKEN_ELEVATION` (added for a different, already-abandoned exploration — see the commented-out
block in `InteropProxy.IsProcessElevated`). CsWin32 has already generated
`Windows.Win32.PInvoke.OpenProcessToken` / `GetTokenInformation` and the full
`Windows.Win32.Security.TOKEN_INFORMATION_CLASS` enum (a transitively-generated dependent type of
`GetTokenInformation`, which includes `TokenElevationType`) for this project. No `NativeMethods.txt`
change is needed for this task.

- [ ] **Step 1: Replace the hand-written `DllImport`s and elevation-type enum**

In `WinTabber.Interop/UacHelper.cs`, change:

```csharp
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WinTabber.Interop;

internal static class UacHelper
{
    private const string uacRegistryKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";
    private const string uacRegistryValue = "EnableLUA";

    private static uint STANDARD_RIGHTS_READ = 0x00020000;
    private static uint TOKEN_QUERY = 0x0008;
    private static uint TOKEN_READ = (STANDARD_RIGHTS_READ | TOKEN_QUERY);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenProcessToken(IntPtr ProcessHandle, UInt32 DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    public enum TOKEN_INFORMATION_CLASS
    {
        TokenUser = 1,
        TokenGroups,
        TokenPrivileges,
        TokenOwner,
        TokenPrimaryGroup,
        TokenDefaultDacl,
        TokenSource,
        TokenType,
        TokenImpersonationLevel,
        TokenStatistics,
        TokenRestrictedSids,
        TokenSessionId,
        TokenGroupsAndPrivileges,
        TokenSessionReference,
        TokenSandBoxInert,
        TokenAuditPolicy,
        TokenOrigin,
        TokenElevationType,
        TokenLinkedToken,
        TokenElevation,
        TokenHasRestrictions,
        TokenAccessInformation,
        TokenVirtualizationAllowed,
        TokenVirtualizationEnabled,
        TokenIntegrityLevel,
        TokenUIAccess,
        TokenMandatoryPolicy,
        TokenLogonSid,
        MaxTokenInfoClass
    }

    public enum TOKEN_ELEVATION_TYPE
    {
        TokenElevationTypeDefault = 1,
        TokenElevationTypeFull,
        TokenElevationTypeLimited
    }
```

to:

```csharp
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Security;

namespace WinTabber.Interop;

internal static class UacHelper
{
    private const string uacRegistryKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";
    private const string uacRegistryValue = "EnableLUA";

    public enum TOKEN_ELEVATION_TYPE
    {
        TokenElevationTypeDefault = 1,
        TokenElevationTypeFull,
        TokenElevationTypeLimited
    }
```

(`TOKEN_INFORMATION_CLASS` is deleted entirely — every reference to it in this file switches to
the CsWin32-generated `Windows.Win32.Security.TOKEN_INFORMATION_CLASS`, whose members carry the
same names, including `TokenElevationType`. `TOKEN_ELEVATION_TYPE` stays — it's this file's own
result type, not a Win32 type CsWin32 generates.)

- [ ] **Step 2: Rewrite `IsProcessElevated(int)`**

Change:

```csharp
    public static bool IsProcessElevated(int processId)
    {
        if (IsUacEnabled)
        {
            IntPtr tokenHandle;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_READ, out tokenHandle))
            {
                throw new ApplicationException("Could not get process token.  Win32 Error Code: " + Marshal.GetLastWin32Error());
            }

            TOKEN_ELEVATION_TYPE elevationResult = TOKEN_ELEVATION_TYPE.TokenElevationTypeDefault;

            int elevationResultSize = Marshal.SizeOf((int)elevationResult);
            uint returnedSize = 0;
            IntPtr elevationTypePtr = Marshal.AllocHGlobal(elevationResultSize);

            bool success = GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenElevationType, elevationTypePtr, (uint)elevationResultSize, out returnedSize);
            if (success)
            {
                elevationResult = (TOKEN_ELEVATION_TYPE)Marshal.ReadInt32(elevationTypePtr);
                bool isProcessAdmin = elevationResult == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
                return isProcessAdmin;
            }
            else
            {
                throw new ApplicationException("Unable to determine the current elevation.");
            }
        }
        else
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            bool result = principal.IsInRole(WindowsBuiltInRole.Administrator);
            return result;
        }
    }
```

to:

```csharp
    public static unsafe bool IsProcessElevated(int processId)
    {
        if (IsUacEnabled)
        {
            using var currentProcess = Process.GetCurrentProcess();
            if (!PInvoke.OpenProcessToken(currentProcess.SafeHandle, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var tokenHandle))
            {
                throw new ApplicationException("Could not get process token.  Win32 Error Code: " + Marshal.GetLastWin32Error());
            }

            using (tokenHandle)
            {
                TOKEN_ELEVATION_TYPE elevationResult = TOKEN_ELEVATION_TYPE.TokenElevationTypeDefault;
                uint returnedSize = 0;

                bool success = PInvoke.GetTokenInformation(
                    tokenHandle,
                    TOKEN_INFORMATION_CLASS.TokenElevationType,
                    &elevationResult,
                    (uint)sizeof(TOKEN_ELEVATION_TYPE),
                    &returnedSize);

                if (success)
                {
                    return elevationResult == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
                }
                else
                {
                    throw new ApplicationException("Unable to determine the current elevation.");
                }
            }
        }
        else
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            bool result = principal.IsInRole(WindowsBuiltInRole.Administrator);
            return result;
        }
    }
```

`PInvoke.OpenProcessToken`'s CsWin32 signature returns a `SafeFileHandle` via `out` and accepts a
`SafeHandle` for the process handle (matching the pattern already used in
`InteropProxy.EnableDebugPrivilege`, which calls
`PInvoke.OpenProcessToken(currentProcess.SafeHandle, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES |
TOKEN_ACCESS_MASK.TOKEN_QUERY, out var hToken)` — copy that call shape). `PInvoke.GetTokenInformation`
takes a raw pointer + size rather than `IntPtr`/`Marshal.AllocHGlobal`; the build is the arbiter
on the exact generated parameter types — adjust pointer/`void*` casts if the compiler disagrees
with the signature shown here.

- [ ] **Step 3: Rewrite `IsProcessElevated(Process)` the same way**

Apply the identical transformation to the second overload,
`IsProcessElevated(Process process)`, using `process.SafeHandle` instead of
`Process.GetCurrentProcess().Handle`, and keeping its existing outer `try`/`catch { return true;
}` wrapper (the two overloads are near-duplicates already; this task does not deduplicate them
further — that's out of scope, YAGNI, unless a future task needs it).

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors. Elevation-check logic is unchanged; only the P/Invoke mechanism
changed. If pointer/marshaling types don't match what CsWin32 generated, fix from the compiler
error, not by guessing further.

- [ ] **Step 5: Manual smoke check**

There is no automated test for `UacHelper` (no seam — this is exactly the P/Invoke-wrapper
surface the spec's T5.4 section says to leave untested). Run the app
(`dotnet run --project WinTabberUI/WinTabberUI.csproj`) and suspend/resume a non-elevated window
via the existing suspend hotkey/UI to confirm `IsProcessElevated` still gates suspension
correctly (a regression here would make `CanSuspend` wrongly allow or refuse elevated
processes).

- [ ] **Step 6: Commit**

```bash
git add WinTabber.Interop/UacHelper.cs
git commit -m "chore: Phase 5A T7 — migrate UacHelper's token-elevation check to CsWin32"
```

---

### Task 8: Relocate `SetWindowCompositionAttribute` into `WinTabber.Interop`

**Files:**
- Modify: `WinTabber.Interop/PInvoke.cs` (or create `WinTabber.Interop/ChromeInterop.cs` — see
  Step 1)
- Modify: `WinTabber.UI.Common/Chrome/Interop.cs`
- Modify: `WinTabber.UI.Common/WinTabber.UI.Common.csproj`
- Move: `WinTabber.UI.Common/Chrome/WindowCompositionAttributeData.cs` →
  `WinTabber.Interop/WindowCompositionAttributeData.cs`
- Move: `WinTabber.UI.Common/Chrome/WindowCompositionAttribute.cs` →
  `WinTabber.Interop/WindowCompositionAttribute.cs`

**Interfaces:** None public — this is an internal relocation within the `Interop`/`UI.Common`
boundary.

- [ ] **Step 1: Create the wrapper in `WinTabber.Interop`**

Create `WinTabber.Interop/ChromeInterop.cs`:

```csharp
using System.Runtime.InteropServices;

namespace WinTabber.Interop;

/// <summary>
/// Undocumented user32 export used to set window chrome attributes (e.g. blur-behind). Not present
/// in CsWin32 metadata. Lives here per CLAUDE.md's Windows Interop rule: hand-written declarations
/// for undocumented APIs live in WinTabber.Interop regardless of what they act on.
/// </summary>
public static class ChromeInterop
{
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static void SetWindowCompositionAttribute(IntPtr hwnd, WindowCompositionAttributeData data) =>
        SetWindowCompositionAttribute(hwnd, ref data);
}
```

- [ ] **Step 2: Move the two supporting types**

```bash
git mv WinTabber.UI.Common/Chrome/WindowCompositionAttributeData.cs WinTabber.Interop/WindowCompositionAttributeData.cs
git mv WinTabber.UI.Common/Chrome/WindowCompositionAttribute.cs WinTabber.Interop/WindowCompositionAttribute.cs
```

Update the namespace in both moved files from `WinTabber.UI.Common.Chrome` to `WinTabber.Interop`.

- [ ] **Step 3: Add the project reference**

In `WinTabber.UI.Common/WinTabber.UI.Common.csproj`, add to the existing `<ItemGroup>` with
`ProjectReference`s:

```xml
      <ProjectReference Include="..\WinTabber.Interop\WinTabber.Interop.csproj" />
```

- [ ] **Step 4: Update `UI.Common/Chrome/Interop.cs`**

Change:

```csharp
using System.Runtime.InteropServices;

namespace WinTabber.UI.Common.Chrome;

internal class Interop
{
    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);


    public static void EnableBlur(nint handle, AccentState accentState, uint color)
    {
        
    }

    internal static void SetAccentPolicy(IntPtr hWnd, AccentState accentState, AccentFlags accentFlags, uint gradientColor)
    {

        var accent = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = accentFlags,
            AnimationId = 0,
            GradientColor = gradientColor
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };


        SetWindowCompositionAttribute(hWnd, ref data);

        Marshal.FreeHGlobal(accentPtr);
    }


}
```

to:

```csharp
using System.Runtime.InteropServices;
using WinTabber.Interop;

namespace WinTabber.UI.Common.Chrome;

internal class Interop
{
    public static void EnableBlur(nint handle, AccentState accentState, uint color)
    {

    }

    internal static void SetAccentPolicy(IntPtr hWnd, AccentState accentState, AccentFlags accentFlags, uint gradientColor)
    {

        var accent = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = accentFlags,
            AnimationId = 0,
            GradientColor = gradientColor
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        ChromeInterop.SetWindowCompositionAttribute(hWnd, data);

        Marshal.FreeHGlobal(accentPtr);
    }
}
```

`WindowCompositionAttributeData` and `WindowCompositionAttribute` are now in `WinTabber.Interop`
(no longer `WinTabber.UI.Common.Chrome`) — the `using WinTabber.Interop;` added above resolves
both, since this file also references them by bare name (`WindowCompositionAttributeData`,
`WindowCompositionAttribute.WCA_ACCENT_POLICY`). `AccentPolicy`, `AccentState`, `AccentFlags`
stay in `WinTabber.UI.Common.Chrome` (already in the same namespace as this file, unaffected).

- [ ] **Step 5: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Manual smoke check**

No automated test exercises window blur/chrome (WPF chrome code, per T3.1's testability
argument). Run the app and confirm any window using blur-behind chrome (check
`AccentHelper.EnableBlur` callers via `grep -rn "AccentHelper.EnableBlur"` if unsure which window
uses it) still renders the blur effect correctly.

- [ ] **Step 7: Commit**

```bash
git add WinTabber.Interop/ WinTabber.UI.Common/
git commit -m "chore: Phase 5A T8 — relocate SetWindowCompositionAttribute into WinTabber.Interop"
```

---

### Task 9: Update CLAUDE.md's Windows Interop section

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:** None (documentation only).

- [ ] **Step 1: Narrow the own-window-rendering carve-out**

In CLAUDE.md's **Windows Interop** section, find the paragraph beginning "Win32 that **affects
the rendering of our own windows**..." and add a clause narrowing it to CsWin32-backed APIs,
plus a new paragraph stating the hand-written-imports-consolidate-in-Interop rule. The exact
wording is left to the implementer, but it must state, at minimum:

1. The own-window-rendering carve-out (own windows stay with the WPF code owning `HwndSource`,
   each with its own `NativeMethods.txt`) applies to **CsWin32-backed** Win32 only.
2. A **hand-written** `DllImport` for an undocumented API always lives in `WinTabber.Interop`,
   regardless of what it acts on — cite `SetWindowCompositionAttribute` (now in
   `WinTabber.Interop/ChromeInterop.cs`) as the example, alongside the pre-existing
   `NtNativeMethods.cs` and `PInvoke.cs`'s `DwmpActivateLivePreview`.
3. Update the four-interface split (`IProcessControl`, `IWindowVisibility`, `IWindowPlacement`,
   `IWindowInterop`) into the doc wherever it currently says the interface is `IInteropProxy` —
   check `WinTabber.Interop/IInteropProxy.cs` no longer exists, so any doc reference to that
   filename is now stale.

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "chore: Phase 5A T9 — update CLAUDE.md's Windows Interop section for the split and the narrowed chrome carve-out"
```

---

### Task 10: Make debug-window-after-media-window ordering structural

**Files:**
- Modify: `WinTabberUI/Coordinators/ViewCoordinatorBase.cs`
- Modify: `WinTabberUI/Coordinators/MediaDebugWindowCoordinator.cs`
- Modify: `WinTabberUI/BackgroundServiceContainer.cs` (comment only)

**Interfaces:**
- Produces: `ViewCoordinatorBase<T>.ShownChanges` — `IObservable<bool>`, emits the coordinator's
  actual shown/hidden state after `Show()`/`Close()` run (not the raw upstream trigger).

- [ ] **Step 1: Add `ShownChanges` to `ViewCoordinatorBase<T>`**

In `WinTabberUI/Coordinators/ViewCoordinatorBase.cs`, add a backing subject and expose it. Change:

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Windows;

namespace WinTabberUI.Coordinators
{
    public abstract class ViewCoordinatorBase<T> : IDisposable where T : Window
    {
        public ViewCoordinatorBase(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        private IDisposable _listener = null!;

        private T? _instance;
        private IServiceProvider _serviceProvider;
```

to:

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows;

namespace WinTabberUI.Coordinators
{
    public abstract class ViewCoordinatorBase<T> : IDisposable where T : Window
    {
        public ViewCoordinatorBase(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        private IDisposable _listener = null!;

        private T? _instance;
        private IServiceProvider _serviceProvider;
        private readonly BehaviorSubject<bool> _shownChanges = new(false);

        /// <summary>
        /// Reflects this coordinator's actual shown/hidden state, updated after Show()/Close() run —
        /// not the raw trigger from GetChangeEvents(). A dependent coordinator that needs "has this
        /// coordinator actually shown its window" should observe this instead of independently
        /// re-deriving the same condition from a shared upstream subject, which would make
        /// correctness depend on subscribe order (see MediaDebugWindowCoordinator).
        /// </summary>
        public IObservable<bool> ShownChanges => _shownChanges.AsObservable();
```

Then update `CloseCore`/`ShowCore` to push into the subject. Change:

```csharp
        private void CloseCore()
        {
            if (IsShown)
            {
                Close(_instance);

            }
            if (!ReuseInstances)
            {
                _instance = null;
            }
        }

        private void ShowCore()
        {
            if (!IsShown)
            {
                _instance = GetInstance();
                _instance.Closed += _instance_Closed;
                Show(_instance);
            }
        }
```

to:

```csharp
        private void CloseCore()
        {
            if (IsShown)
            {
                Close(_instance);
                _shownChanges.OnNext(false);
            }
            if (!ReuseInstances)
            {
                _instance = null;
            }
        }

        private void ShowCore()
        {
            if (!IsShown)
            {
                _instance = GetInstance();
                _instance.Closed += _instance_Closed;
                Show(_instance);
                _shownChanges.OnNext(true);
            }
        }
```

Also dispose the subject in `Dispose()`:

```csharp
        public void Dispose()
        {
            _listener.Dispose();
            Release();
        }
```

to:

```csharp
        public void Dispose()
        {
            _listener.Dispose();
            Release();
            _shownChanges.Dispose();
        }
```

- [ ] **Step 2: Update `MediaDebugWindowCoordinator` to depend on `MediaWindowViewCoordinator`**

Change:

```csharp
using System.Reactive.Linq;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows the media debug window together with the media controls window, but only while the tray
/// menu toggle is on.
/// </summary>
public class MediaDebugWindowCoordinator : ViewCoordinatorBase<MediaDebugWindow>
{
    private readonly ApplicationStateViewModel _vm;
    private readonly MediaDebugStateService _debugState;

    public MediaDebugWindowCoordinator(
        ApplicationStateViewModel vm,
        MediaDebugStateService debugState,
        IServiceProvider provider
    )
        : base(provider)
    {
        ReuseInstances = true;
        _vm = vm;
        _debugState = debugState;
    }

    protected override IObservable<bool> GetChangeEvents()
    {
        // Both sources replay their current value, so turning the toggle on while the media window
        // is already open opens the debug window at once.
        return _vm
            .IsMediaControlsActiveChanges.CombineLatest(
                _debugState.IsEnabledChanges,
                (isMediaVisible, isDebugEnabled) => isMediaVisible && isDebugEnabled
            )
            .DistinctUntilChanged();
    }
```

to:

```csharp
using System.Reactive.Linq;
using WinTabberUI.Services;
using WinTabberUI.Views;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows the media debug window together with the media controls window, but only while the tray
/// menu toggle is on. Depends on MediaWindowViewCoordinator directly (rather than independently
/// re-deriving media-window visibility from the same upstream subject) so this coordinator reacts
/// to the media window's actual shown state — its position in BackgroundServiceContainer's
/// composite no longer has to come after MediaWindowViewCoordinator's for correctness.
/// </summary>
public class MediaDebugWindowCoordinator : ViewCoordinatorBase<MediaDebugWindow>
{
    private readonly MediaWindowViewCoordinator _mediaWindowCoordinator;
    private readonly MediaDebugStateService _debugState;

    public MediaDebugWindowCoordinator(
        MediaWindowViewCoordinator mediaWindowCoordinator,
        MediaDebugStateService debugState,
        IServiceProvider provider
    )
        : base(provider)
    {
        ReuseInstances = true;
        _mediaWindowCoordinator = mediaWindowCoordinator;
        _debugState = debugState;
    }

    protected override IObservable<bool> GetChangeEvents()
    {
        // ShownChanges is a BehaviorSubject (replays its current value), so turning the toggle on
        // while the media window is already open opens the debug window at once.
        return _mediaWindowCoordinator
            .ShownChanges.CombineLatest(
                _debugState.IsEnabledChanges,
                (isMediaVisible, isDebugEnabled) => isMediaVisible && isDebugEnabled
            )
            .DistinctUntilChanged();
    }
```

(The `using WinTabberUI.ViewModels;` import is dropped since `ApplicationStateViewModel` is no
longer referenced in this file — verify the build doesn't need it for anything else in the file
before removing; if another symbol in this file still needs it, keep it.)

- [ ] **Step 3: Update the comment in `BackgroundServiceContainer.cs`**

Change the comment above `MediaDebugWindowCoordinator` in the `CompositeDisposable(...)` list
from:

```csharp
            // Must come after MediaWindowViewCoordinator: both react to the same visibility
            // subject, and the debug window must not open before the window it observes.
            ioc.GetRequiredService<MediaDebugWindowCoordinator>().Init(),
```

to:

```csharp
            // No longer order-dependent: MediaDebugWindowCoordinator observes
            // MediaWindowViewCoordinator's own ShownChanges directly, not the same upstream
            // subject, so its position in this list doesn't affect correctness.
            ioc.GetRequiredService<MediaDebugWindowCoordinator>().Init(),
```

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Manual smoke check**

No automated test covers `ViewCoordinatorBase`/`MediaDebugWindowCoordinator` (WPF-coupled, per
T3.1's testability argument — same reasoning as the chrome code). Run the app, enable the media
debug window toggle from the tray menu, trigger the media controls hotkey, and confirm the debug
window still appears alongside the media controls window in the same visible order as before.
Also try reordering the two `.Init()` calls in `BackgroundServiceContainer.cs` temporarily (swap
`MediaWindowViewCoordinator` and `MediaDebugWindowCoordinator` lines), rebuild, and confirm the
debug window still opens correctly after the media window — this proves the fix actually removed
the ordering dependency. Revert the temporary swap before committing.

- [ ] **Step 6: Commit**

```bash
git add WinTabberUI/Coordinators/ WinTabberUI/BackgroundServiceContainer.cs
git commit -m "chore: Phase 5A T10 — MediaDebugWindowCoordinator observes MediaWindowViewCoordinator directly"
```

---

### Task 11: Move `EnableDebugPrivilege` into `ProcessSuspensionService`'s constructor

**Files:**
- Modify: `WinTabber.Api.Windowing/Suspension/ProcessSuspensionService.cs`
- Modify: `WinTabberUI/BackgroundServiceContainer.cs`

**Interfaces:** None new — uses `IProcessControl` (Task 1), already a constructor parameter of
`ProcessSuspensionService` since Task 3.

- [ ] **Step 1: Call `EnableDebugPrivilege()` in `ProcessSuspensionService`'s constructor**

In `WinTabber.Api.Windowing/Suspension/ProcessSuspensionService.cs`, in the constructor (after
Task 3's changes), add the call right after the field assignments, before the startup-pruning
loop:

```csharp
    public ProcessSuspensionService(
        IProcessControl processControl,
        IWindowVisibility windowVisibility,
        IProcessRepository processRepository,
        ISuspendedWindowStore store,
        IEnumerable<ISuspensionStrategy> strategies
    )
    {
        _processControl = processControl;
        _windowVisibility = windowVisibility;
        _processRepository = processRepository;
        _store = store;
        _strategies = strategies as IReadOnlyList<ISuspensionStrategy> ?? strategies.ToList();
        _defaultStrategy = _strategies[0];

        // Must happen before any suspend attempt; OpenProcess on another user's process needs it.
        // Established here (not by the DI container's construction order) so this precondition
        // holds regardless of where this service is resolved from.
        _processControl.EnableDebugPrivilege();

        // Startup pruning: drop entries whose PID no longer resolves or whose image-path hash
        // no longer matches (PID reused by an unrelated process).
        var pruned = new List<SuspendedWindowEntry>();
```

- [ ] **Step 2: Remove the call from `BackgroundServiceContainer.cs`**

Change:

```csharp
        ioc.GetRequiredService<AppCache>().Load();
        // Must happen before any suspend attempt; OpenProcess on another user's process needs it.
        ioc.GetRequiredService<IInteropProxy>().EnableDebugPrivilege();
```

to:

```csharp
        ioc.GetRequiredService<AppCache>().Load();
```

(Note: this line still says `IInteropProxy` in the current source — it should already have been
fixed to a concrete interface by Task 5/9's compile-fix pass if it wasn't caught there; if
`BackgroundServiceContainer.cs` still references `IInteropProxy` at this point, that means Task 5
missed this call site — check `grep -n "IInteropProxy" WinTabberUI/BackgroundServiceContainer.cs`
before starting this task and fix it as part of this step if needed, since this line is being
deleted anyway.)

Also remove the now-unused `IProcessSuspensionService` note if any — check whether
`ioc.GetRequiredService<IProcessSuspensionService>()` further down in the same
`CompositeDisposable(...)` list needs its own comment updated; it currently reads:

```csharp
            // Disposing this resumes every frozen process on exit. Order within the composite is
            // insertion order and does not matter here: ResumeAll only touches IInteropProxy and
            // the state file, neither of which the composite owns.
            ioc.GetRequiredService<IProcessSuspensionService>(),
```

Update the stale `IInteropProxy` mention:

```csharp
            // Disposing this resumes every frozen process on exit. Order within the composite is
            // insertion order and does not matter here: ResumeAll only touches IProcessControl,
            // IWindowVisibility, and the state file, none of which the composite owns.
            ioc.GetRequiredService<IProcessSuspensionService>(),
```

- [ ] **Step 3: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: 81 passed, 0 failed. `ProcessSuspensionServiceTests.cs`'s `FakeProcessControl` already
implements `EnableDebugPrivilege()` as a no-op (from Task 3), so the constructor call added here
is exercised by every existing test that constructs a `ProcessSuspensionService` without any
test change needed.

- [ ] **Step 5: Manual smoke check**

Run the app and suspend/resume a process via the existing hotkey/UI, confirming suspension still
works end-to-end (this proves `EnableDebugPrivilege` still actually runs before the first
suspend attempt, just from a different call site).

- [ ] **Step 6: Commit**

```bash
git add WinTabber.Api.Windowing/Suspension/ProcessSuspensionService.cs WinTabberUI/BackgroundServiceContainer.cs
git commit -m "chore: Phase 5A T11 — ProcessSuspensionService enables debug privilege in its own constructor"
```

---

## Final verification

- [ ] Run `rm -rf */bin */obj` (or the PowerShell equivalent) and rebuild from scratch —
  `.cleanup/HANDOFF.md` notes WPF's generated `.g.cs` can mask a stale reference under
  incremental compilation, and this plan touches `.xaml.cs`-adjacent files
  (`SuspendedWindowsWindow.xaml.cs`, `MediaDebugWindow.xaml.cs`) and a project reference change
  (`UI.Common` → `Interop`).
- [ ] `dotnet build WinTabber.slnx` — 0 warnings, 0 errors.
- [ ] `dotnet test --solution WinTabber.slnx` — 81 passed, 0 failed, 0 skipped.
- [ ] `grep -rn "IInteropProxy" --include=*.cs .` returns zero matches outside
  `.cleanup/`/`docs/`/`CLAUDE.md` (historical/doc references only).
- [ ] Manual smoke test: launch the app, exercise window switching, media controls + debug
  window, and process suspend/resume once each.
