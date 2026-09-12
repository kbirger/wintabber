# Elevation Backend Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a configurable elevation-backend abstraction (built-in one-shot elevator, or
gsudo with its credentials cache) behind one interface, generalize the elevator to support both
closing and minimizing windows, and fix Focus Select's previously-unnoticed gap where it silently
fails to minimize elevated windows.

**Architecture:** `IElevationLauncher` (new, `WinTabber.Interop`) abstracts "run this action on
these window handles, elevated" behind `BuiltInElevationLauncher` (today's `runas` launch,
generalized) and `GsudoElevationLauncher` (new: launches via gsudo, with a short-lived credentials
cache). `ElevationLauncherResolver` picks between them per a settings-driven
`IElevationBackendProvider`. The elevation-partition-and-batch logic moves from
`ApplicationRef.CloseAllWindows` onto `WindowManager`, shared by both close-all and Focus Select's
minimize-others.

**Tech Stack:** .NET 10, CsWin32, TUnit, ReactiveUI, Microsoft.Extensions.DependencyInjection.

**Spec:** `docs/superpowers/specs/2026-09-12-elevation-backend-abstraction-design.md`

## Global Constraints

- Two elevation backends (`BuiltIn`, `Gsudo`), chosen via `GeneralSettings.ElevationBackend`,
  default `BuiltIn`. Neither replaces the other.
- gsudo's cache is enabled by default whenever `Gsudo` is selected, using a short, non-default
  30-second duration (`gsudo cache on -d 30`), not gsudo's 5-minute default.
- `IElevationBackendProvider` (not the concrete `GeneralSettings` type) is what
  `ElevationLauncherResolver` depends on, so the whole abstraction — enums, interfaces, both
  launchers, the resolver — stays inside `WinTabber.Interop`, with no reference to
  `WinTabber.Infrastructure`.
- gsudo detection is a PATH probe for `gsudo.exe` — this is gsudo's own documented install
  mechanism ("adding gsudo to the PATH").
- The gsudo-not-found install flow runs `winget install --id gerardog.gsudo` (the `--id` is
  required — a bare `winget install gsudo` resolves to an unrelated Microsoft Store listing).
- `GsudoElevationLauncher` must be registered as a DI **singleton** — its cache-started flag is
  only meaningful if the same instance persists for the app's lifetime.
- Thin Win32/external-process-launch glue (the elevator's verb dispatch, both launchers' actual
  process-starting code, the winget shell-out) is not unit tested, per this repo's established
  precedent (`WinTabber.Interop.Tests`' documented narrow scope) — manual verification covers it.
  The two genuinely new pieces of *pure* logic (`ElevationLauncherResolver`'s backend selection,
  `WindowManager`'s elevation partitioning) do get real tests.

---

### Task 1: Generalize `WinTabber.Elevator` to close or minimize

**Files:**
- Modify: `WinTabber.Elevator/Program.cs`
- Modify: `WinTabber.Elevator/NativeMethods.txt`

**Interfaces:**
- Produces: `WinTabber.Elevator.exe <verb> <handle1> <handle2> ...` where `<verb>` is `"close"` or
  `"minimize"`. Later tasks' launchers pass this shape.

No unit test — thin Win32-calling glue, per this repo's established precedent. Verification is a
solution build.

- [ ] **Step 1: Replace `Program.cs`**

Current content:

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;

foreach (var arg in args)
{
    if (!int.TryParse(arg, out var handle))
    {
        continue;
    }

    var hwnd = new HWND(handle);
    if (PInvoke.IsWindow(hwnd))
    {
        PInvoke.PostMessage(hwnd, PInvoke.WM_CLOSE, 0, 0);
    }
}
```

Replace with:

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

if (args.Length < 2)
{
    return;
}

var action = args[0];

for (var i = 1; i < args.Length; i++)
{
    if (!int.TryParse(args[i], out var handle))
    {
        continue;
    }

    var hwnd = new HWND(handle);
    if (!PInvoke.IsWindow(hwnd))
    {
        continue;
    }

    switch (action)
    {
        case "close":
            PInvoke.PostMessage(hwnd, PInvoke.WM_CLOSE, 0, 0);
            break;
        case "minimize":
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
            break;
    }
}
```

- [ ] **Step 2: Add the new CsWin32 declarations**

`WinTabber.Elevator/NativeMethods.txt` currently:

```
PostMessage
IsWindow
WM_CLOSE
```

Replace with:

```
PostMessage
IsWindow
WM_CLOSE
ShowWindow
SW_MINIMIZE
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add WinTabber.Elevator/Program.cs WinTabber.Elevator/NativeMethods.txt
git commit -m "$(cat <<'EOF'
feat: generalize WinTabber.Elevator to close or minimize

The elevated helper now takes an action verb ("close"/"minimize") as its
first argument instead of always closing, so it can serve Focus Select's
elevation gap the same way it already serves close-all.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `IElevationLauncher` abstraction core + `BuiltInElevationLauncher`

**Files:**
- Create: `WinTabber.Interop/ElevatedWindowAction.cs`
- Create: `WinTabber.Interop/ElevationBackend.cs`
- Create: `WinTabber.Interop/IElevationLauncher.cs`
- Create: `WinTabber.Interop/BuiltInElevationLauncher.cs`
- Modify: `WinTabber.Interop/IWindowInterop.cs`
- Modify: `WinTabber.Interop/InteropProxy.cs`
- Modify: `WinTabber.Api.Windowing/ApplicationRef.cs`
- Modify: `WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs`
- Modify: `WinTabber.Api.Windowing.Tests/ApplicationRefTests.cs`
- Modify: `WinTabberUI/Bootstrapper.cs`

**Interfaces:**
- Consumes: `WinTabber.Elevator.exe`'s verb argument from Task 1.
- Produces: `IElevationLauncher` (`bool IsAvailable { get; }`,
  `void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)`), `ElevatedWindowAction`
  (`Close`, `Minimize`), `ElevationBackend` (`BuiltIn`, `Gsudo`), and
  `IWindowInterop.RunElevatedAction(ElevatedWindowAction, IEnumerable<int>)` — later tasks build on
  all four names exactly as given here.

This task's Bootstrapper registration is **interim**: it wires `IElevationLauncher` straight to
`BuiltInElevationLauncher` so the app keeps building and running. Task 7 replaces that one line
with the full resolver wiring once `GsudoElevationLauncher` and `ElevationLauncherResolver` exist.

- [ ] **Step 1: Create the new enums**

`WinTabber.Interop/ElevatedWindowAction.cs`:

```csharp
namespace WinTabber.Interop;

public enum ElevatedWindowAction
{
    Close,
    Minimize,
}
```

`WinTabber.Interop/ElevationBackend.cs`:

```csharp
namespace WinTabber.Interop;

public enum ElevationBackend
{
    BuiltIn,
    Gsudo,
}
```

- [ ] **Step 2: Create `IElevationLauncher`**

`WinTabber.Interop/IElevationLauncher.cs`:

```csharp
namespace WinTabber.Interop;

/// <summary>
/// Runs an action against a batch of window handles from an elevated context, bridging the UIPI
/// boundary that blocks WM_CLOSE/ShowWindow/etc. from this (non-elevated) process to a
/// higher-integrity window. Implementations decide *how* to get elevated; callers only care that
/// all of <paramref name="handles"/> get <paramref name="action"/> applied, batched into as few
/// elevation prompts as the implementation can manage.
/// </summary>
public interface IElevationLauncher
{
    /// <summary>Whether this launcher's backend can currently be used (e.g. an external tool is installed).</summary>
    bool IsAvailable { get; }

    void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles);
}
```

- [ ] **Step 3: Create `BuiltInElevationLauncher`**

`WinTabber.Interop/BuiltInElevationLauncher.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace WinTabber.Interop;

/// <summary>
/// Launches the bundled <c>WinTabber.Elevator</c> helper elevated via <c>ShellExecute</c>/<c>runas</c>,
/// one process per call, batching every handle into a single invocation (one UAC prompt per call).
/// Always available — it's our own bundled binary, not an external dependency.
/// </summary>
public class BuiltInElevationLauncher : IElevationLauncher
{
    public bool IsAvailable => true;

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)
    {
        var handleList = handles as IReadOnlyCollection<int> ?? handles.ToList();
        if (handleList.Count == 0)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "WinTabber.Elevator.exe"),
                UseShellExecute = true,
                Verb = "runas",
            };

            startInfo.ArgumentList.Add(action.ToString().ToLowerInvariant());
            foreach (var handle in handleList)
            {
                startInfo.ArgumentList.Add(handle.ToString());
            }

            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // UAC declined (ERROR_CANCELLED), or the elevator binary is missing/broken. Same end
            // state either way: these windows simply stay open.
        }
    }
}
```

- [ ] **Step 4: Replace `IWindowInterop.CloseElevatedWindows` with `RunElevatedAction`**

In `WinTabber.Interop/IWindowInterop.cs`, find:

```csharp
    /// <summary>
    /// Closes windows belonging to an elevated process, which <see cref="CloseWindow"/> cannot
    /// reach — Windows' UIPI blocks WM_CLOSE from this (non-elevated) process to a higher-integrity
    /// window. Launches the <c>WinTabber.Elevator</c> helper elevated (one UAC prompt) with all of
    /// <paramref name="handles"/> batched into a single invocation; the helper posts WM_CLOSE to
    /// each from an elevated context and exits. Fire-and-forget: does not wait for the helper to
    /// exit. If the UAC prompt is declined, or the helper can't be launched at all, this silently
    /// does nothing — matching how <c>WindowRef.MoveTo</c> already treats elevated windows it
    /// can't touch.
    /// </summary>
    void CloseElevatedWindows(IEnumerable<int> handles);
}
```

Replace with:

```csharp
    /// <summary>
    /// Runs <paramref name="action"/> against windows belonging to an elevated process, which
    /// <see cref="CloseWindow"/>/<see cref="MinimizeWindow"/> cannot reach directly — Windows' UIPI
    /// blocks window messages from this (non-elevated) process to a higher-integrity window.
    /// Delegates to whichever <see cref="IElevationLauncher"/> backend is configured, batching all
    /// of <paramref name="handles"/> into as few elevation prompts as that backend can manage.
    /// Fire-and-forget: does not wait for the elevated process to exit. If elevation is declined,
    /// or the backend can't be launched at all, this silently does nothing — matching how
    /// <c>WindowRef.MoveTo</c> already treats elevated windows it can't touch.
    /// </summary>
    void RunElevatedAction(ElevatedWindowAction action, IEnumerable<int> handles);
}
```

- [ ] **Step 5: Update `InteropProxy`**

In `WinTabber.Interop/InteropProxy.cs`, the class currently has no explicit constructor. Find:

```csharp
public class InteropProxy : IProcessControl, IWindowPlacement, IWindowInterop
{
    public void BringWindowToFront(int handle)
```

Replace with:

```csharp
public class InteropProxy : IProcessControl, IWindowPlacement, IWindowInterop
{
    private readonly IElevationLauncher _elevationLauncher;

    public InteropProxy(IElevationLauncher elevationLauncher)
    {
        _elevationLauncher = elevationLauncher;
    }

    public void BringWindowToFront(int handle)
```

Then find the existing `CloseElevatedWindows` method:

```csharp
    public void CloseElevatedWindows(IEnumerable<int> handles)
    {
        var handleList = handles as IReadOnlyCollection<int> ?? handles.ToList();
        if (handleList.Count == 0)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "WinTabber.Elevator.exe"),
                UseShellExecute = true,
                Verb = "runas",
            };

            foreach (var handle in handleList)
            {
                startInfo.ArgumentList.Add(handle.ToString());
            }

            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // UAC declined (ERROR_CANCELLED), or the elevator binary is missing/broken. Same end
            // state either way: these windows simply stay open.
        }
    }
```

Replace it entirely with (the launching logic now lives in `BuiltInElevationLauncher` from Step 3;
`InteropProxy` just delegates):

```csharp
    public void RunElevatedAction(ElevatedWindowAction action, IEnumerable<int> handles) =>
        _elevationLauncher.RunElevated(action, handles);
```

- [ ] **Step 6: Update `ApplicationRef`'s call site**

In `WinTabber.Api.Windowing/ApplicationRef.cs`, add `using WinTabber.Interop;` to the top of the
file (currently only has `using System.Diagnostics;` and `using System.Reactive.Linq;`):

```csharp
using System.Diagnostics;
using System.Reactive.Linq;
using WinTabber.Interop;

namespace WinTabber.Api.Windowing;
```

Then find:

```csharp
        if (elevatedHandles.Count > 0)
        {
            Manager.Interop.CloseElevatedWindows(elevatedHandles);
        }
    }
}
```

Replace with:

```csharp
        if (elevatedHandles.Count > 0)
        {
            Manager.Interop.RunElevatedAction(ElevatedWindowAction.Close, elevatedHandles);
        }
    }
}
```

- [ ] **Step 7: Update the test fake**

In `WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs`, find:

```csharp
    public List<int> ClosedHandles { get; } = [];
    public List<IReadOnlyList<int>> ElevatedCloseCalls { get; } = [];

    public bool IsProcessElevated(Process process) => ElevatedProcesses.Contains(process);

    public void CloseWindow(int handle) => ClosedHandles.Add(handle);

    public void CloseElevatedWindows(IEnumerable<int> handles) =>
        ElevatedCloseCalls.Add(handles.ToList());
```

Replace with:

```csharp
    public List<int> ClosedHandles { get; } = [];
    public List<(ElevatedWindowAction Action, IReadOnlyList<int> Handles)> ElevatedActionCalls { get; } = [];

    public bool IsProcessElevated(Process process) => ElevatedProcesses.Contains(process);

    public void CloseWindow(int handle) => ClosedHandles.Add(handle);

    public void RunElevatedAction(ElevatedWindowAction action, IEnumerable<int> handles) =>
        ElevatedActionCalls.Add((action, handles.ToList()));
```

- [ ] **Step 8: Update the existing tests for the renamed fake members**

`WinTabber.Api.Windowing.Tests/ApplicationRefTests.cs` currently:

```csharp
using System.Diagnostics;
using WinTabber.Api.Windowing.Tests.Fakes;

namespace WinTabber.Api.Windowing.Tests;

public class ApplicationRefTests
{
    [Test]
    public async Task CloseAllWindows_ClosesNonElevatedDirectly_AndBatchesElevatedIntoOneCall()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        // Two windows on one elevated process, one window on a separate, non-elevated process —
        // Process.GetCurrentProcess() returns a fresh object each call, which is all that's
        // needed since FakeWindowInterop compares by reference, not PID.
        var elevatedProcess = Process.GetCurrentProcess();
        var normalProcess = Process.GetCurrentProcess();
        interop.ElevatedProcesses.Add(elevatedProcess);

        var elevatedProcessRef = new WindowProcessRef(elevatedProcess, application);
        var normalProcessRef = new WindowProcessRef(normalProcess, application);

        var windowA = new WindowRef(101, elevatedProcessRef);
        var windowB = new WindowRef(102, elevatedProcessRef);
        var windowC = new WindowRef(201, normalProcessRef);

        application.CloseAllWindows([windowA, windowB, windowC]);

        await Assert.That(interop.ClosedHandles.Count).IsEqualTo(1);
        await Assert.That(interop.ClosedHandles).Contains(201);
        await Assert.That(interop.ElevatedCloseCalls.Count).IsEqualTo(1);
        await Assert.That(interop.ElevatedCloseCalls[0].Count).IsEqualTo(2);
        await Assert.That(interop.ElevatedCloseCalls[0]).Contains(101);
        await Assert.That(interop.ElevatedCloseCalls[0]).Contains(102);
    }

    [Test]
    public async Task CloseAllWindows_NoElevatedWindows_NeverCallsCloseElevatedWindows()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        var normalProcessRef = new WindowProcessRef(Process.GetCurrentProcess(), application);
        var window = new WindowRef(301, normalProcessRef);

        application.CloseAllWindows([window]);

        await Assert.That(interop.ClosedHandles).Contains(301);
        await Assert.That(interop.ElevatedCloseCalls.Count).IsEqualTo(0);
    }
}
```

Replace entirely with:

```csharp
using System.Diagnostics;
using WinTabber.Api.Windowing.Tests.Fakes;
using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests;

public class ApplicationRefTests
{
    [Test]
    public async Task CloseAllWindows_ClosesNonElevatedDirectly_AndBatchesElevatedIntoOneCall()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        // Two windows on one elevated process, one window on a separate, non-elevated process —
        // Process.GetCurrentProcess() returns a fresh object each call, which is all that's
        // needed since FakeWindowInterop compares by reference, not PID.
        var elevatedProcess = Process.GetCurrentProcess();
        var normalProcess = Process.GetCurrentProcess();
        interop.ElevatedProcesses.Add(elevatedProcess);

        var elevatedProcessRef = new WindowProcessRef(elevatedProcess, application);
        var normalProcessRef = new WindowProcessRef(normalProcess, application);

        var windowA = new WindowRef(101, elevatedProcessRef);
        var windowB = new WindowRef(102, elevatedProcessRef);
        var windowC = new WindowRef(201, normalProcessRef);

        application.CloseAllWindows([windowA, windowB, windowC]);

        await Assert.That(interop.ClosedHandles.Count).IsEqualTo(1);
        await Assert.That(interop.ClosedHandles).Contains(201);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(1);
        await Assert.That(interop.ElevatedActionCalls[0].Action).IsEqualTo(ElevatedWindowAction.Close);
        await Assert.That(interop.ElevatedActionCalls[0].Handles.Count).IsEqualTo(2);
        await Assert.That(interop.ElevatedActionCalls[0].Handles).Contains(101);
        await Assert.That(interop.ElevatedActionCalls[0].Handles).Contains(102);
    }

    [Test]
    public async Task CloseAllWindows_NoElevatedWindows_NeverCallsRunElevatedAction()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        var normalProcessRef = new WindowProcessRef(Process.GetCurrentProcess(), application);
        var window = new WindowRef(301, normalProcessRef);

        application.CloseAllWindows([window]);

        await Assert.That(interop.ClosedHandles).Contains(301);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(0);
    }
}
```

- [ ] **Step 9: Interim Bootstrapper wiring**

In `WinTabberUI/Bootstrapper.cs`, find:

```csharp
            .AddSingleton<InteropProxy>()
            .AddSingleton<IProcessControl>(sp => sp.GetRequiredService<InteropProxy>())
```

Replace with:

```csharp
            .AddSingleton<BuiltInElevationLauncher>()
            .AddSingleton<IElevationLauncher>(sp => sp.GetRequiredService<BuiltInElevationLauncher>())
            .AddSingleton<InteropProxy>()
            .AddSingleton<IProcessControl>(sp => sp.GetRequiredService<InteropProxy>())
```

(This is interim — Task 7 replaces the `IElevationLauncher` line with the full resolver wiring
once `GsudoElevationLauncher` and `ElevationLauncherResolver` exist.)

- [ ] **Step 10: Build and run the full test suite**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run:
```bash
dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj
dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj
dotnet test WinTabber.Infrastructure.Tests/WinTabber.Infrastructure.Tests.csproj
dotnet test WinTabber.Events.Tests/WinTabber.Events.Tests.csproj
```
Expected: all four `Passed!` — `WinTabber.Api.Windowing.Tests` at 20/20 (the two renamed tests
still pass, unchanged behavior), the other three unaffected at their existing counts (7, 20, 43).

- [ ] **Step 11: Commit**

```bash
git add WinTabber.Interop/ElevatedWindowAction.cs WinTabber.Interop/ElevationBackend.cs WinTabber.Interop/IElevationLauncher.cs WinTabber.Interop/BuiltInElevationLauncher.cs WinTabber.Interop/IWindowInterop.cs WinTabber.Interop/InteropProxy.cs WinTabber.Api.Windowing/ApplicationRef.cs WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs WinTabber.Api.Windowing.Tests/ApplicationRefTests.cs WinTabberUI/Bootstrapper.cs
git commit -m "$(cat <<'EOF'
feat: introduce IElevationLauncher abstraction with a built-in launcher

Generalizes IWindowInterop.CloseElevatedWindows into RunElevatedAction,
delegating to a pluggable IElevationLauncher. BuiltInElevationLauncher
carries today's runas launch, generalized to pass an action verb. Wiring
is interim (always BuiltIn) until the gsudo launcher and resolver land.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Centralize elevation-aware partitioning on `WindowManager` (TDD)

**Files:**
- Modify: `WinTabber.Api.Windowing/WindowManager.cs`
- Modify: `WinTabber.Api.Windowing/ApplicationRef.cs`
- Modify: `WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs`
- Create: `WinTabber.Api.Windowing.Tests/WindowManagerTests.cs`

**Interfaces:**
- Consumes: `IWindowInterop.RunElevatedAction` from Task 2.
- Produces: `WindowManager.CloseWindows(IEnumerable<WindowRef>)` and
  `WindowManager.MinimizeWindows(IEnumerable<WindowRef>)` — Task 4's Focus Select wiring calls
  `MinimizeWindows` directly.

- [ ] **Step 1: Make `FakeWindowInterop.MinimizeWindow` actually track calls**

It currently throws, since nothing needed it before. In
`WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs`, find:

```csharp
    public void MinimizeWindow(int handle) => throw new NotSupportedException();
```

Replace with:

```csharp
    public List<int> MinimizedHandles { get; } = [];

    public void MinimizeWindow(int handle) => MinimizedHandles.Add(handle);
```

- [ ] **Step 2: Write the failing tests**

`WinTabber.Api.Windowing.Tests/WindowManagerTests.cs`:

```csharp
using System.Diagnostics;
using WinTabber.Api.Windowing.Tests.Fakes;
using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests;

public class WindowManagerTests
{
    [Test]
    public async Task MinimizeWindows_MinimizesNonElevatedDirectly_AndBatchesElevatedIntoOneCall()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        var elevatedProcess = Process.GetCurrentProcess();
        var normalProcess = Process.GetCurrentProcess();
        interop.ElevatedProcesses.Add(elevatedProcess);

        var elevatedProcessRef = new WindowProcessRef(elevatedProcess, application);
        var normalProcessRef = new WindowProcessRef(normalProcess, application);

        var windowA = new WindowRef(401, elevatedProcessRef);
        var windowB = new WindowRef(402, normalProcessRef);

        manager.MinimizeWindows([windowA, windowB]);

        await Assert.That(interop.MinimizedHandles.Count).IsEqualTo(1);
        await Assert.That(interop.MinimizedHandles).Contains(402);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(1);
        await Assert.That(interop.ElevatedActionCalls[0].Action).IsEqualTo(ElevatedWindowAction.Minimize);
        await Assert.That(interop.ElevatedActionCalls[0].Handles).Contains(401);
    }

    [Test]
    public async Task MinimizeWindows_NoElevatedWindows_NeverCallsRunElevatedAction()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        var normalProcessRef = new WindowProcessRef(Process.GetCurrentProcess(), application);
        var window = new WindowRef(501, normalProcessRef);

        manager.MinimizeWindows([window]);

        await Assert.That(interop.MinimizedHandles).Contains(501);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(0);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj -- --treenode-filter "/*/*/WindowManagerTests/*"`
Expected: build error / FAIL — `WindowManager` has no member `MinimizeWindows` yet.

- [ ] **Step 4: Implement `CloseWindows`/`MinimizeWindows` on `WindowManager`**

In `WinTabber.Api.Windowing/WindowManager.cs`, find:

```csharp
    public void EndPreview()
    {
        Interop.DeactivateLivePreview();
    }
}
```

Replace with:

```csharp
    public void EndPreview()
    {
        Interop.DeactivateLivePreview();
    }

    /// <summary>
    /// Closes every window in <paramref name="windows"/>. Non-elevated windows close directly
    /// (WM_CLOSE); elevated ones can't be reached that way (UIPI), so their handles are batched
    /// into a single elevated launch — one prompt per call, not one per elevated window.
    /// </summary>
    public void CloseWindows(IEnumerable<WindowRef> windows) => PerformAction(windows, ElevatedWindowAction.Close);

    /// <summary>
    /// Minimizes every window in <paramref name="windows"/>, with the same elevation-aware
    /// batching <see cref="CloseWindows"/> uses — minimizing an elevated window is blocked by UIPI
    /// exactly like closing one is.
    /// </summary>
    public void MinimizeWindows(IEnumerable<WindowRef> windows) => PerformAction(windows, ElevatedWindowAction.Minimize);

    private void PerformAction(IEnumerable<WindowRef> windows, ElevatedWindowAction action)
    {
        var elevatedHandles = new List<int>();

        foreach (var window in windows)
        {
            if (window.Process.IsProcessElevated)
            {
                elevatedHandles.Add(window.Handle);
                continue;
            }

            switch (action)
            {
                case ElevatedWindowAction.Close:
                    window.Close();
                    break;
                case ElevatedWindowAction.Minimize:
                    window.Minimize();
                    break;
            }
        }

        if (elevatedHandles.Count > 0)
        {
            Interop.RunElevatedAction(action, elevatedHandles);
        }
    }
}
```

(`WinTabber.Interop` is already imported at the top of this file — `using WinTabber.Interop;` on
line 2 — so no new using is needed.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj -- --treenode-filter "/*/*/WindowManagerTests/*"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Simplify `ApplicationRef.CloseAllWindows` to delegate**

In `WinTabber.Api.Windowing/ApplicationRef.cs`, find:

```csharp
    /// <summary>
    /// Closes every window in <paramref name="windows"/>. Non-elevated windows close directly
    /// (WM_CLOSE); elevated ones can't be reached that way (UIPI), so their handles are batched
    /// into a single call to <see cref="IWindowInterop.CloseElevatedWindows"/> — one UAC prompt
    /// per call to this method, not one per elevated window.
    /// </summary>
    public void CloseAllWindows(IEnumerable<WindowRef> windows)
    {
        var elevatedHandles = new List<int>();

        foreach (var window in windows)
        {
            AssertOwnsWindow(window);

            if (window.Process.IsProcessElevated)
            {
                elevatedHandles.Add(window.Handle);
            }
            else
            {
                window.Close();
            }
        }

        if (elevatedHandles.Count > 0)
        {
            Manager.Interop.RunElevatedAction(ElevatedWindowAction.Close, elevatedHandles);
        }
    }
}
```

Replace with:

```csharp
    /// <summary>
    /// Closes every window in <paramref name="windows"/>, provided this application owns all of
    /// them. Delegates the actual elevation-aware close to <see cref="WindowManager.CloseWindows"/>.
    /// </summary>
    public void CloseAllWindows(IEnumerable<WindowRef> windows)
    {
        var windowList = windows as IReadOnlyCollection<WindowRef> ?? windows.ToList();

        foreach (var window in windowList)
        {
            AssertOwnsWindow(window);
        }

        Manager.CloseWindows(windowList);
    }
}
```

- [ ] **Step 7: Run the full existing test suite to confirm no regression**

Run: `dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj`
Expected: `Passed! - total: 22` (the 20 that existed before this task, plus the 2 new
`WindowManagerTests`; `ApplicationRefTests`' 2 tests still pass unchanged, now exercising the
delegating implementation).

- [ ] **Step 8: Commit**

```bash
git add WinTabber.Api.Windowing/WindowManager.cs WinTabber.Api.Windowing/ApplicationRef.cs WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs WinTabber.Api.Windowing.Tests/WindowManagerTests.cs
git commit -m "$(cat <<'EOF'
feat: centralize elevation-aware close/minimize on WindowManager

ApplicationRef.CloseAllWindows becomes a thin ownership-checking wrapper
over the new WindowManager.CloseWindows/MinimizeWindows, so Focus Select
can reuse the same elevation partitioning for minimize.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Wire Focus Select's minimize-others through `WindowManager`

**Files:**
- Modify: `WinTabberUI/ViewModels/WindowSelectorViewModel.cs`

**Interfaces:**
- Consumes: `WindowManager.MinimizeWindows(IEnumerable<WindowRef>)` from Task 3.

No test — `WinTabberUI` has no test project. Verification is a solution build.

- [ ] **Step 1: Replace `MinimizeOthers`'s direct `.Minimize()` loops**

Find:

```csharp
    /// <summary>Minimizes everything except <paramref name="selected" />, per Focus Select's scope setting.</summary>
    private void MinimizeOthers(WindowItem selected)
    {
        if (_settings.FocusSelectScope == FocusSelectScope.AllWindows)
        {
            // Same enumeration DockWindow already uses live in the UI, so it's proven fast enough
            // interactively; it also already excludes our own process's windows. A synthesized
            // Win+Home was tried here first, but the modifier that triggers Focus Select is by
            // definition still held when this runs, so the OS saw e.g. Ctrl+Win+Home and never
            // fired its own "minimize all but active" gesture.
            foreach (var window in WindowManager.GetWindows())
            {
                if (window.Handle != selected.Handle)
                {
                    window.Minimize();
                }
            }
            return;
        }

        foreach (var item in WindowItems)
        {
            if (item != selected)
            {
                item.WindowRef.Minimize();
            }
        }
    }
```

Replace with:

```csharp
    /// <summary>
    /// Minimizes everything except <paramref name="selected" />, per Focus Select's scope setting.
    /// Elevation-aware via <see cref="WindowManager.MinimizeWindows" /> — an elevated window can't
    /// be minimized by a direct call any more than it can be closed directly (UIPI).
    /// </summary>
    private void MinimizeOthers(WindowItem selected)
    {
        if (_settings.FocusSelectScope == FocusSelectScope.AllWindows)
        {
            // Same enumeration DockWindow already uses live in the UI, so it's proven fast enough
            // interactively; it also already excludes our own process's windows. A synthesized
            // Win+Home was tried here first, but the modifier that triggers Focus Select is by
            // definition still held when this runs, so the OS saw e.g. Ctrl+Win+Home and never
            // fired its own "minimize all but active" gesture.
            WindowManager.MinimizeWindows(
                WindowManager.GetWindows().Where(window => window.Handle != selected.Handle));
            return;
        }

        WindowManager.MinimizeWindows(WindowItems.Where(item => item != selected).Select(item => item.WindowRef));
    }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add WinTabberUI/ViewModels/WindowSelectorViewModel.cs
git commit -m "$(cat <<'EOF'
fix: make Focus Select's minimize-others elevation-aware

MinimizeOthers previously called WindowRef.Minimize() directly, which UIPI
silently blocks for elevated windows — the same gap close-all had before
this backend abstraction. Both scope modes now route through
WindowManager.MinimizeWindows.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: `GsudoElevationLauncher`

**Files:**
- Create: `WinTabber.Interop/GsudoElevationLauncher.cs`

**Interfaces:**
- Consumes: `IElevationLauncher`, `ElevatedWindowAction` from Task 2; `WinTabber.Elevator.exe`'s
  verb argument from Task 1.
- Produces: `GsudoElevationLauncher` (public class implementing `IElevationLauncher`) — Task 6's
  DI wiring and Task 8's settings ViewModel both construct/consume it directly.

No unit test — external-process-launch glue (gsudo/winget shell-outs aren't something this repo's
test suite exercises headlessly), consistent with `BuiltInElevationLauncher`. Verification is a
solution build; full functional verification is manual (see the plan-wide manual verification
checklist at the end of Task 8).

- [ ] **Step 1: Create the launcher**

`WinTabber.Interop/GsudoElevationLauncher.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace WinTabber.Interop;

/// <summary>
/// Launches the bundled elevator via gsudo instead of a direct runas prompt, so repeat elevations
/// within a short window reuse gsudo's own credentials cache instead of prompting every time.
/// Requires gsudo (https://github.com/gerardog/gsudo) on PATH — <see cref="IsAvailable" /> reports
/// whether it's found there.
/// </summary>
public class GsudoElevationLauncher : IElevationLauncher
{
    private bool _cacheStarted;

    public bool IsAvailable => TryResolveGsudoPath() is not null;

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)
    {
        var handleList = handles as IReadOnlyCollection<int> ?? handles.ToList();
        if (handleList.Count == 0)
        {
            return;
        }

        var gsudoPath = TryResolveGsudoPath();
        if (gsudoPath is null)
        {
            return;
        }

        EnsureCacheStarted(gsudoPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = gsudoPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "WinTabber.Elevator.exe"));
        startInfo.ArgumentList.Add(action.ToString().ToLowerInvariant());
        foreach (var handle in handleList)
        {
            startInfo.ArgumentList.Add(handle.ToString());
        }

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // gsudo declined, disappeared between the availability check and here, or failed to
            // launch for some other reason. Same silent end state as the built-in path.
        }
    }

    /// <summary>
    /// Starts a short-lived (30 second) gsudo credentials-cache session, once per process
    /// lifetime, so repeat elevations within that window don't each show their own UAC prompt.
    /// Deliberately shorter than gsudo's 5-minute default (<c>gsudo config CacheDuration</c>) to
    /// bound how long a compromised process in this app's process tree could silently elevate.
    /// </summary>
    private void EnsureCacheStarted(string gsudoPath)
    {
        if (_cacheStarted)
        {
            return;
        }

        try
        {
            var cacheStartInfo = new ProcessStartInfo
            {
                FileName = gsudoPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            cacheStartInfo.ArgumentList.Add("cache");
            cacheStartInfo.ArgumentList.Add("on");
            cacheStartInfo.ArgumentList.Add("-d");
            cacheStartInfo.ArgumentList.Add("30");

            using var process = Process.Start(cacheStartInfo);
            process?.WaitForExit();
            _cacheStarted = true;
        }
        catch (Win32Exception)
        {
            // Couldn't start the cache session (e.g. declined). Leave _cacheStarted false so the
            // next call tries again; the RunElevated call that follows still works — it just also
            // shows gsudo's own (uncached) prompt for this one action.
        }
    }

    private static string? TryResolveGsudoPath()
    {
        // gsudo's own install docs confirm this is the detection mechanism: "No Windows service is
        // required or system change is done, except adding gsudo to the PATH."
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var candidate = Path.Combine(dir, "gsudo.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add WinTabber.Interop/GsudoElevationLauncher.cs
git commit -m "$(cat <<'EOF'
feat: add GsudoElevationLauncher

Launches the elevator through gsudo instead of a direct runas prompt,
lazily starting a short (30s) credentials-cache session so repeat
elevations within that window don't re-prompt. Not yet wired into the app
— that's Task 7.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: `IElevationBackendProvider` + `ElevationLauncherResolver` (TDD)

**Files:**
- Create: `WinTabber.Interop/IElevationBackendProvider.cs`
- Create: `WinTabber.Interop/ElevationLauncherResolver.cs`
- Create: `WinTabber.Interop.Tests/Fakes/FakeElevationLauncher.cs`
- Create: `WinTabber.Interop.Tests/Fakes/FakeElevationBackendProvider.cs`
- Create: `WinTabber.Interop.Tests/ElevationLauncherResolverTests.cs`

**Interfaces:**
- Consumes: `IElevationLauncher`, `ElevatedWindowAction`, `ElevationBackend` from Task 2.
- Produces: `IElevationBackendProvider` (`ElevationBackend Backend { get; }`) and
  `ElevationLauncherResolver` (constructor `(IElevationLauncher builtIn, IElevationLauncher gsudo,
  IElevationBackendProvider backendProvider)`, implements `IElevationLauncher`) — Task 7's
  Bootstrapper wiring constructs it with the concrete `BuiltInElevationLauncher` and
  `GsudoElevationLauncher` instances, and a settings-backed provider.

- [ ] **Step 1: Create `IElevationBackendProvider`**

`WinTabber.Interop/IElevationBackendProvider.cs`:

```csharp
namespace WinTabber.Interop;

/// <summary>
/// Supplies the user's configured elevation backend choice to <see cref="ElevationLauncherResolver" />
/// without that resolver needing a direct reference to wherever settings actually live.
/// </summary>
public interface IElevationBackendProvider
{
    ElevationBackend Backend { get; }
}
```

- [ ] **Step 2: Write the fakes**

`WinTabber.Interop.Tests/Fakes/FakeElevationLauncher.cs`:

```csharp
namespace WinTabber.Interop.Tests.Fakes;

public sealed class FakeElevationLauncher : IElevationLauncher
{
    public bool IsAvailable { get; set; } = true;
    public List<(ElevatedWindowAction Action, IReadOnlyList<int> Handles)> Calls { get; } = [];

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles) =>
        Calls.Add((action, handles.ToList()));
}
```

`WinTabber.Interop.Tests/Fakes/FakeElevationBackendProvider.cs`:

```csharp
namespace WinTabber.Interop.Tests.Fakes;

public sealed class FakeElevationBackendProvider : IElevationBackendProvider
{
    public ElevationBackend Backend { get; set; } = ElevationBackend.BuiltIn;
}
```

- [ ] **Step 3: Write the failing tests**

`WinTabber.Interop.Tests/ElevationLauncherResolverTests.cs`:

```csharp
using WinTabber.Interop.Tests.Fakes;

namespace WinTabber.Interop.Tests;

public class ElevationLauncherResolverTests
{
    [Test]
    public async Task RunElevated_BackendBuiltIn_AlwaysUsesBuiltInLauncher()
    {
        var builtIn = new FakeElevationLauncher();
        var gsudo = new FakeElevationLauncher { IsAvailable = true };
        var backendProvider = new FakeElevationBackendProvider { Backend = ElevationBackend.BuiltIn };
        var resolver = new ElevationLauncherResolver(builtIn, gsudo, backendProvider);

        resolver.RunElevated(ElevatedWindowAction.Close, [123]);

        await Assert.That(builtIn.Calls.Count).IsEqualTo(1);
        await Assert.That(gsudo.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RunElevated_BackendGsudoAndAvailable_UsesGsudoLauncher()
    {
        var builtIn = new FakeElevationLauncher();
        var gsudo = new FakeElevationLauncher { IsAvailable = true };
        var backendProvider = new FakeElevationBackendProvider { Backend = ElevationBackend.Gsudo };
        var resolver = new ElevationLauncherResolver(builtIn, gsudo, backendProvider);

        resolver.RunElevated(ElevatedWindowAction.Minimize, [456]);

        await Assert.That(gsudo.Calls.Count).IsEqualTo(1);
        await Assert.That(gsudo.Calls[0].Action).IsEqualTo(ElevatedWindowAction.Minimize);
        await Assert.That(builtIn.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RunElevated_BackendGsudoButNotAvailable_FallsBackToBuiltInLauncher()
    {
        var builtIn = new FakeElevationLauncher();
        var gsudo = new FakeElevationLauncher { IsAvailable = false };
        var backendProvider = new FakeElevationBackendProvider { Backend = ElevationBackend.Gsudo };
        var resolver = new ElevationLauncherResolver(builtIn, gsudo, backendProvider);

        resolver.RunElevated(ElevatedWindowAction.Close, [789]);

        await Assert.That(builtIn.Calls.Count).IsEqualTo(1);
        await Assert.That(gsudo.Calls.Count).IsEqualTo(0);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj -- --treenode-filter "/*/*/ElevationLauncherResolverTests/*"`
Expected: build error / FAIL — `ElevationLauncherResolver` doesn't exist yet.

- [ ] **Step 5: Implement `ElevationLauncherResolver`**

`WinTabber.Interop/ElevationLauncherResolver.cs`:

```csharp
namespace WinTabber.Interop;

/// <summary>
/// Picks which <see cref="IElevationLauncher" /> backend actually runs an elevated action, per the
/// user's configured <see cref="ElevationBackend" /> — falling back to the built-in launcher if
/// <see cref="ElevationBackend.Gsudo" /> is configured but not actually available (e.g. gsudo
/// isn't installed).
/// </summary>
public class ElevationLauncherResolver : IElevationLauncher
{
    private readonly IElevationLauncher _builtIn;
    private readonly IElevationLauncher _gsudo;
    private readonly IElevationBackendProvider _backendProvider;

    public ElevationLauncherResolver(IElevationLauncher builtIn, IElevationLauncher gsudo, IElevationBackendProvider backendProvider)
    {
        _builtIn = builtIn;
        _gsudo = gsudo;
        _backendProvider = backendProvider;
    }

    public bool IsAvailable => true;

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)
    {
        var active = _backendProvider.Backend == ElevationBackend.Gsudo && _gsudo.IsAvailable ? _gsudo : _builtIn;
        active.RunElevated(action, handles);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj -- --treenode-filter "/*/*/ElevationLauncherResolverTests/*"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 7: Run the full `WinTabber.Interop.Tests` suite to confirm no regression**

Run: `dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj`
Expected: `Passed! - total: 10` (the 7 that existed before this task, plus the 3 new
`ElevationLauncherResolverTests`).

- [ ] **Step 8: Commit**

```bash
git add WinTabber.Interop/IElevationBackendProvider.cs WinTabber.Interop/ElevationLauncherResolver.cs WinTabber.Interop.Tests/Fakes/FakeElevationLauncher.cs WinTabber.Interop.Tests/Fakes/FakeElevationBackendProvider.cs WinTabber.Interop.Tests/ElevationLauncherResolverTests.cs
git commit -m "$(cat <<'EOF'
feat: add ElevationLauncherResolver with backend fallback

Picks the built-in or gsudo launcher per IElevationBackendProvider,
falling back to built-in when gsudo is selected but unavailable. Not yet
wired into the app's DI container — that's Task 7.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Settings field + Bootstrapper final wiring

**Files:**
- Modify: `WinTabber.Infrastructure/WinTabber.Infrastructure.csproj`
- Modify: `WinTabber.Infrastructure/Settings/GeneralSettings.cs`
- Modify: `WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs`
- Modify: `WinTabberUI/Views/GeneralSettingsPage.xaml`
- Create: `WinTabberUI/Services/GeneralSettingsElevationBackendProvider.cs`
- Modify: `WinTabberUI/Bootstrapper.cs`

**Interfaces:**
- Consumes: `ElevationBackend` (Task 2), `GsudoElevationLauncher` (Task 5),
  `ElevationLauncherResolver`/`IElevationBackendProvider` (Task 6).
- Produces: `GeneralSettings.ElevationBackend` — Task 8's install-flow UI reads/writes it via the
  same `GeneralSettingsViewModel.ElevationBackend` property this task adds.

No new automated test — this is settings/DI plumbing in `WinTabberUI`, which has no test project.
Verification is a solution build plus the plan-wide manual checklist at the end of Task 8.

- [ ] **Step 1: Reference `WinTabber.Interop` from `WinTabber.Infrastructure`**

`WinTabber.Infrastructure.csproj` already references `WinTabber.Events` for
`GeneralSettings.FocusSelectModifier`'s type; it needs the same explicit reference for
`ElevationBackend`. Find:

```xml
    <ItemGroup>
        <ProjectReference Include="..\WinTabber.Api.Media\WinTabber.Api.Media.csproj" />
        <ProjectReference Include="..\WinTabber.Events\WinTabber.Events.csproj" />
    </ItemGroup>
```

Replace with:

```xml
    <ItemGroup>
        <ProjectReference Include="..\WinTabber.Api.Media\WinTabber.Api.Media.csproj" />
        <ProjectReference Include="..\WinTabber.Events\WinTabber.Events.csproj" />
        <ProjectReference Include="..\WinTabber.Interop\WinTabber.Interop.csproj" />
    </ItemGroup>
```

- [ ] **Step 2: Add the setting**

In `WinTabber.Infrastructure/Settings/GeneralSettings.cs`, find:

```csharp
using WinTabber.Events.Shortcuts;
using WinTabberUI.Services;

namespace WinTabberUI.Models.Settings
{
    public class GeneralSettings
    {
```

Replace with:

```csharp
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabberUI.Services;

namespace WinTabberUI.Models.Settings
{
    public class GeneralSettings
    {
```

Then find the end of the class:

```csharp
        /// <summary>Which windows Focus Select minimizes.</summary>
        public FocusSelectScope FocusSelectScope { get; set; } = FocusSelectScope.SwitcherWindows;
    }
}
```

Replace with:

```csharp
        /// <summary>Which windows Focus Select minimizes.</summary>
        public FocusSelectScope FocusSelectScope { get; set; } = FocusSelectScope.SwitcherWindows;

        /// <summary>Which backend closes/minimizes windows belonging to an elevated process.</summary>
        public ElevationBackend ElevationBackend { get; set; } = ElevationBackend.BuiltIn;
    }
}
```

- [ ] **Step 3: Add the ViewModel property**

In `WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs`, find:

```csharp
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
using WinTabber.Events.Shortcuts;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;
```

Replace with:

```csharp
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;
```

Find the constructor body:

```csharp
            EnableFocusSelect = settings.EnableFocusSelect;
            FocusSelectModifier = settings.FocusSelectModifier;
            FocusSelectScope = settings.FocusSelectScope;
        }
```

Replace with:

```csharp
            EnableFocusSelect = settings.EnableFocusSelect;
            FocusSelectModifier = settings.FocusSelectModifier;
            FocusSelectScope = settings.FocusSelectScope;
            ElevationBackend = settings.ElevationBackend;
        }
```

Find the backing-field declarations:

```csharp
        private ShortcutModifiers _focusSelectModifier;
        private FocusSelectScope _focusSelectScope;
        private GeneralSettings _settings;
```

Replace with:

```csharp
        private ShortcutModifiers _focusSelectModifier;
        private FocusSelectScope _focusSelectScope;
        private ElevationBackend _elevationBackend;
        private GeneralSettings _settings;
```

Find the end of the class:

```csharp
        public FocusSelectScope[] FocusSelectScopes => Enum.GetValues<FocusSelectScope>();
    }
}
```

Replace with:

```csharp
        public FocusSelectScope[] FocusSelectScopes => Enum.GetValues<FocusSelectScope>();

        public ElevationBackend ElevationBackend
        {
            get => _elevationBackend;
            set
            {
                _settings.ElevationBackend = value;
                this.RaiseAndSetIfChanged(ref _elevationBackend, value);
            }
        }

        public ElevationBackend[] ElevationBackends => Enum.GetValues<ElevationBackend>();
    }
}
```

- [ ] **Step 4: Add the settings-page combo box**

In `WinTabberUI/Views/GeneralSettingsPage.xaml`, find:

```xml
                    <ui:SettingsCard Header="Focus select scope" Margin="0,8,0,0"
                            Description="Which windows get minimized: only the ones the switcher showed, or every other window">
                        <ComboBox MinWidth="220" x:Name="FocusSelectScopeList"
                                VerticalAlignment="Center"
                                FontSize="14"
                                ItemsSource="{Binding FocusSelectScopes}"
                                SelectedValue="{Binding Path=FocusSelectScope, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    </ui:SettingsCard>
                </StackPanel>
            </HeaderedContentControl>
        </StackPanel>
    </Grid>
</rxwpf:ReactivePage>
```

Replace with:

```xml
                    <ui:SettingsCard Header="Focus select scope" Margin="0,8,0,0"
                            Description="Which windows get minimized: only the ones the switcher showed, or every other window">
                        <ComboBox MinWidth="220" x:Name="FocusSelectScopeList"
                                VerticalAlignment="Center"
                                FontSize="14"
                                ItemsSource="{Binding FocusSelectScopes}"
                                SelectedValue="{Binding Path=FocusSelectScope, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    </ui:SettingsCard>
                    <ui:SettingsCard Header="Elevation backend" Margin="0,8,0,0"
                            Description="How WinTabber closes or minimizes windows belonging to an elevated (admin) process">
                        <ComboBox MinWidth="220" x:Name="ElevationBackendList"
                                VerticalAlignment="Center"
                                FontSize="14"
                                ItemsSource="{Binding ElevationBackends}"
                                SelectedValue="{Binding Path=ElevationBackend, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    </ui:SettingsCard>
                </StackPanel>
            </HeaderedContentControl>
        </StackPanel>
    </Grid>
</rxwpf:ReactivePage>
```

- [ ] **Step 5: Create the settings adapter**

`WinTabberUI/Services/GeneralSettingsElevationBackendProvider.cs`:

```csharp
using WinTabber.Interop;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.Services;

/// <summary>
/// Adapts <see cref="GeneralSettings.ElevationBackend" /> to <see cref="IElevationBackendProvider" />
/// so <c>WinTabber.Interop</c>'s <see cref="ElevationLauncherResolver" /> never needs a direct
/// reference to settings.
/// </summary>
public class GeneralSettingsElevationBackendProvider : IElevationBackendProvider
{
    private readonly GeneralSettings _settings;

    public GeneralSettingsElevationBackendProvider(ApplicationSettings settings)
    {
        _settings = settings.General;
    }

    public ElevationBackend Backend => _settings.ElevationBackend;
}
```

- [ ] **Step 6: Replace the interim Bootstrapper wiring**

In `WinTabberUI/Bootstrapper.cs`, find:

```csharp
            .AddSingleton<BuiltInElevationLauncher>()
            .AddSingleton<IElevationLauncher>(sp => sp.GetRequiredService<BuiltInElevationLauncher>())
            .AddSingleton<InteropProxy>()
```

Replace with:

```csharp
            .AddSingleton<BuiltInElevationLauncher>()
            .AddSingleton<GsudoElevationLauncher>()
            .AddSingleton<IElevationBackendProvider, GeneralSettingsElevationBackendProvider>()
            .AddSingleton<IElevationLauncher>(sp => new ElevationLauncherResolver(
                sp.GetRequiredService<BuiltInElevationLauncher>(),
                sp.GetRequiredService<GsudoElevationLauncher>(),
                sp.GetRequiredService<IElevationBackendProvider>()))
            .AddSingleton<InteropProxy>()
```

`WinTabberUI.Services` should already be reachable from `Bootstrapper.cs` without a new `using` —
`AutoStartupService` and `InputListenerService`, both in that namespace, are already referenced
unqualified elsewhere in this file. If the build reports `GeneralSettingsElevationBackendProvider`
as unresolvable, add `using WinTabberUI.Services;` to this file's using list.

- [ ] **Step 7: Build and run the full test suite**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run:
```bash
dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj
dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj
dotnet test WinTabber.Infrastructure.Tests/WinTabber.Infrastructure.Tests.csproj
dotnet test WinTabber.Events.Tests/WinTabber.Events.Tests.csproj
```
Expected: all four `Passed!` at their Task 6 counts (22, 10, 20, 43) — this task adds no new
tests, just settings/DI wiring.

- [ ] **Step 8: Commit**

```bash
git add WinTabber.Infrastructure/WinTabber.Infrastructure.csproj WinTabber.Infrastructure/Settings/GeneralSettings.cs WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs WinTabberUI/Views/GeneralSettingsPage.xaml WinTabberUI/Services/GeneralSettingsElevationBackendProvider.cs WinTabberUI/Bootstrapper.cs
git commit -m "$(cat <<'EOF'
feat: expose elevation backend as a setting, wire the resolver into DI

Adds GeneralSettings.ElevationBackend (default BuiltIn) with a combo box
on the General settings page, and replaces the interim
BuiltIn-only IElevationLauncher registration with the full
ElevationLauncherResolver wiring.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: gsudo-not-found install prompt

**Files:**
- Modify: `WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs`
- Modify: `WinTabberUI/ViewModels/SettingsWindowViewModel.cs`
- Modify: `WinTabberUI/Views/GeneralSettingsPage.xaml`

**Interfaces:**
- Consumes: `GsudoElevationLauncher.IsAvailable` (Task 5), `GeneralSettingsViewModel.ElevationBackend`
  (Task 7).

`GeneralSettingsViewModel` is not DI-constructed — it's built with a plain `new` inside
`SettingsViewModel` (in `WinTabberUI/ViewModels/SettingsWindowViewModel.cs`, despite the
filename). Changing its constructor signature means updating that call site too; `SettingsViewModel`
itself *is* registered as a bare `AddSingleton<SettingsViewModel>()` in `Bootstrapper.cs`, so
adding a `GsudoElevationLauncher` constructor parameter there is auto-resolved with no
registration change needed.

No new automated test — this is a UI-triggered external-process shell-out (`winget`), consistent
with this plan's other untested process-launch glue.

- [ ] **Step 1: Add the availability property and install command**

In `WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs`, find the top of the file:

```csharp
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;

namespace WinTabberUI.ViewModels.Settings
{
    public record StartupModeItem(string Name, StartupMode Mode);
    public class GeneralSettingsViewModel : SettingsViewModelBase
    {
        public GeneralSettingsViewModel(GeneralSettings settings)
            : base("General", FluentSystemIcons.Settings_32_Filled)
        {
            _settings = settings;
```

Replace with:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive;
using System.Threading.Tasks;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;

namespace WinTabberUI.ViewModels.Settings
{
    public record StartupModeItem(string Name, StartupMode Mode);
    public class GeneralSettingsViewModel : SettingsViewModelBase
    {
        public GeneralSettingsViewModel(GeneralSettings settings, GsudoElevationLauncher gsudoElevationLauncher)
            : base("General", FluentSystemIcons.Settings_32_Filled)
        {
            _settings = settings;
            _gsudoElevationLauncher = gsudoElevationLauncher;
            IsGsudoAvailable = _gsudoElevationLauncher.IsAvailable;
            InstallGsudoCommand = ReactiveCommand.CreateFromTask(InstallGsudoAsync);
            _showGsudoInstallPrompt = this
                .WhenAnyValue(
                    x => x.ElevationBackend,
                    x => x.IsGsudoAvailable,
                    (backend, available) => backend == ElevationBackend.Gsudo && !available)
                .ToProperty(this, x => x.ShowGsudoInstallPrompt);
```

- [ ] **Step 2: Add the backing field, property, command, and helper method**

Find the end of the class:

```csharp
        public ElevationBackend[] ElevationBackends => Enum.GetValues<ElevationBackend>();
    }
}
```

Replace with:

```csharp
        public ElevationBackend[] ElevationBackends => Enum.GetValues<ElevationBackend>();

        private readonly GsudoElevationLauncher _gsudoElevationLauncher;
        private readonly ObservableAsPropertyHelper<bool> _showGsudoInstallPrompt;
        private bool _isGsudoAvailable;

        public bool IsGsudoAvailable
        {
            get => _isGsudoAvailable;
            private set => this.RaiseAndSetIfChanged(ref _isGsudoAvailable, value);
        }

        /// <summary>True only when Gsudo is the selected backend and it isn't actually installed.</summary>
        public bool ShowGsudoInstallPrompt => _showGsudoInstallPrompt.Value;

        public ReactiveCommand<Unit, Unit> InstallGsudoCommand { get; }

        private async Task InstallGsudoAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("install");
                startInfo.ArgumentList.Add("--id");
                startInfo.ArgumentList.Add("gerardog.gsudo");

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Win32Exception)
            {
                // winget missing, install failed, user cancelled, etc. — IsGsudoAvailable below
                // simply stays false, and the install card stays visible.
            }

            IsGsudoAvailable = _gsudoElevationLauncher.IsAvailable;
        }
    }
}
```

- [ ] **Step 3: Update the `GeneralSettingsViewModel` construction call site**

In `WinTabberUI/ViewModels/SettingsWindowViewModel.cs`, find:

```csharp
using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabberUI.Models.Settings;
using WinTabberUI.ViewModels.Settings;

namespace WinTabberUI.ViewModels;

public class SettingsViewModel : ReactiveObject, IDisposable
{
```

Replace with:

```csharp
using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabberUI.Models.Settings;
using WinTabberUI.ViewModels.Settings;

namespace WinTabberUI.ViewModels;

public class SettingsViewModel : ReactiveObject, IDisposable
{
```

Then find the constructor:

```csharp
    public SettingsViewModel(
        WinTabberEventManager winTabberEventManager,
        ApplicationSettings settings,
        IShortcutMapProvider shortcutMapProvider
    )
    {
        _isShown = new BehaviorSubject<bool>(false);

        CloseCommand = ReactiveCommand.Create(() => _isShown.OnNext(false));
        SaveCommand = ReactiveCommand.Create(() => Save());
        _settings = settings;


        Appearance = new AppearanceSettingsViewModel(_settings.Appearance);
        General = new GeneralSettingsViewModel(_settings.General);
```

Replace with:

```csharp
    public SettingsViewModel(
        WinTabberEventManager winTabberEventManager,
        ApplicationSettings settings,
        IShortcutMapProvider shortcutMapProvider,
        GsudoElevationLauncher gsudoElevationLauncher
    )
    {
        _isShown = new BehaviorSubject<bool>(false);

        CloseCommand = ReactiveCommand.Create(() => _isShown.OnNext(false));
        SaveCommand = ReactiveCommand.Create(() => Save());
        _settings = settings;


        Appearance = new AppearanceSettingsViewModel(_settings.Appearance);
        General = new GeneralSettingsViewModel(_settings.General, gsudoElevationLauncher);
```

- [ ] **Step 4: Add the conditional settings card**

In `WinTabberUI/Views/GeneralSettingsPage.xaml`, add `BoolToVisibilityConverter` to the page's
resources. Find:

```xml
    <Page.Resources>
        <ResourceDictionary>
            <c:EnumValuesConverter x:Key="EnumValuesConverter" />
            <c:StringToEnumConverter x:Key="StringToEnumConverter" />
        </ResourceDictionary>
    </Page.Resources>
```

Replace with:

```xml
    <Page.Resources>
        <ResourceDictionary>
            <c:EnumValuesConverter x:Key="EnumValuesConverter" />
            <c:StringToEnumConverter x:Key="StringToEnumConverter" />
            <c:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
        </ResourceDictionary>
    </Page.Resources>
```

Then find the elevation-backend card added in Task 7:

```xml
                    <ui:SettingsCard Header="Elevation backend" Margin="0,8,0,0"
                            Description="How WinTabber closes or minimizes windows belonging to an elevated (admin) process">
                        <ComboBox MinWidth="220" x:Name="ElevationBackendList"
                                VerticalAlignment="Center"
                                FontSize="14"
                                ItemsSource="{Binding ElevationBackends}"
                                SelectedValue="{Binding Path=ElevationBackend, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    </ui:SettingsCard>
                </StackPanel>
```

Replace with:

```xml
                    <ui:SettingsCard Header="Elevation backend" Margin="0,8,0,0"
                            Description="How WinTabber closes or minimizes windows belonging to an elevated (admin) process">
                        <ComboBox MinWidth="220" x:Name="ElevationBackendList"
                                VerticalAlignment="Center"
                                FontSize="14"
                                ItemsSource="{Binding ElevationBackends}"
                                SelectedValue="{Binding Path=ElevationBackend, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    </ui:SettingsCard>
                    <ui:SettingsCard Header="gsudo not found" Margin="0,8,0,0"
                            Description="Install gsudo via winget to use it as the elevation backend"
                            Visibility="{Binding ShowGsudoInstallPrompt, Converter={StaticResource BoolToVisibilityConverter}}">
                        <Button Content="Install" Command="{Binding InstallGsudoCommand}" />
                    </ui:SettingsCard>
                </StackPanel>
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs WinTabberUI/ViewModels/SettingsWindowViewModel.cs WinTabberUI/Views/GeneralSettingsPage.xaml
git commit -m "$(cat <<'EOF'
feat: offer to install gsudo via winget when it's the selected backend but missing

Adds a conditional settings card that appears only when Gsudo is selected
and not found on PATH, with a button that runs
`winget install --id gerardog.gsudo` and re-checks availability afterward.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 7: Manual verification (cannot be automated)**

This plan's automated coverage stops at build success and the two TDD test suites (`ApplicationRefTests`/`WindowManagerTests`, `ElevationLauncherResolverTests`). Before considering this feature done, verify by hand:

1. Run `dotnet run --project WinTabberUI/WinTabberUI.csproj`. Confirm the General settings page
   shows "Elevation backend" defaulted to `BuiltIn`, and no "gsudo not found" card.
2. With `BuiltIn` selected, run an elevated app (e.g. Task Manager as admin) and trigger close-all
   on it. Expect: one UAC prompt, the window closes — unchanged from before this plan.
3. Trigger Focus Select (hold the configured modifier while committing a selection) with an
   elevated app's window among the others. Expect: it minimizes now, where before this plan it
   silently didn't.
4. Switch the setting to `Gsudo` without gsudo installed. Expect: the "gsudo not found" card
   appears. Click **Install**; expect a winget install to run (may itself show a UAC prompt) and,
   on success, the card disappears once `IsGsudoAvailable` re-checks true.
5. With gsudo installed and `Gsudo` selected, trigger close-all on an elevated app twice within
   30 seconds. Expect: the first shows one UAC prompt (starting the cache), the second shows none.
   Wait over 30 seconds and trigger a third time: expect a new prompt.
6. Repeat step 5's rapid-repeat check for Focus Select's minimize path, not just close-all.
