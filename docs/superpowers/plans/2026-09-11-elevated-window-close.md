# Elevated-Window Close-All Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make close-application-windows actually close windows belonging to elevated (admin)
processes, which currently silently fail because Windows' UIPI blocks `WM_CLOSE` from a
non-elevated sender to an elevated window.

**Architecture:** A new minimal helper executable, `WinTabber.Elevator`, is launched one-shot
(via `ShellExecute`/`runas`) only when a close-all operation actually finds an elevated window.
It receives the target window handles as command-line arguments, posts `WM_CLOSE` to each from
an elevated context, and exits — no persistent process, no IPC. `ApplicationRef.CloseAllWindows`
partitions an application's windows into elevated/non-elevated and batches all elevated handles
into a single elevator launch per close-all operation (one UAC prompt, not one per window).

**Tech Stack:** .NET 10, CsWin32 (P/Invoke source generation), TUnit (test framework).

**Spec:** `docs/superpowers/specs/2026-09-11-elevated-window-close-design.md`

## Global Constraints

- One-shot helper per invocation — never a persistent/long-lived elevated broker process.
- Silent failure on UAC decline or launch failure — no toast, no notification, matches the
  existing `WindowRef.MoveTo`/`ProcessSuspensionService.CanSuspend` precedent of just not
  achieving the action.
- Batch all elevated handles from one close-all operation into a single elevator launch (one UAC
  prompt per operation, not per window).
- `WinTabber.Elevator` must NOT reference `WinTabber.Interop` — it gets its own minimal
  `NativeMethods.txt` (`PostMessage`, `IsWindow`, `WM_CLOSE`) so the one binary in the solution
  that runs elevated has the smallest possible dependency surface.
- `WinTabber.Elevator`'s `OutputType` must be `WinExe`, not `Exe` — a console-subsystem binary
  launched via `ShellExecute`/`runas` shows a visible flashing console window (`CreateNoWindow`
  only suppresses that when `UseShellExecute=false`, which `runas` requires to be `true`).
- Use `ShellExecute`/`Verb="runas"` for elevation, not Windows 11's inbox `sudo` command — `sudo`
  is opt-in (off by default) and its interactive modes aren't suited to a silent background call.

---

### Task 1: `WinTabber.Elevator` helper project

**Files:**
- Create: `WinTabber.Elevator/WinTabber.Elevator.csproj`
- Create: `WinTabber.Elevator/app.manifest`
- Create: `WinTabber.Elevator/NativeMethods.txt`
- Create: `WinTabber.Elevator/Program.cs`
- Modify: `WinTabber.slnx`

**Interfaces:**
- Produces: a standalone executable, `WinTabber.Elevator.exe`, that takes zero or more
  command-line arguments, each expected to parse as an `int` window handle, and posts `WM_CLOSE`
  to each one that is still a valid window. No output, no return-value contract — later tasks
  invoke it fire-and-forget and never inspect its exit code or stdout.

This project has no unit-testable branching logic (parsing an int and calling two P/Invoke
functions) — consistent with this repo's documented precedent of not unit-testing thin Win32
glue (e.g. `WinTabber.Interop.Tests`' own README). Its test cycle here is "it builds", with full
functional verification happening in Task 4's manual check once everything is wired together.

- [ ] **Step 1: Create the project file**

`WinTabber.Elevator/WinTabber.Elevator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net10.0-windows</TargetFramework>
        <ApplicationManifest>app.manifest</ApplicationManifest>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Windows.CsWin32">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
        <None Remove="NativeMethods.txt" />
    </ItemGroup>

    <ItemGroup>
        <AdditionalFiles Include="NativeMethods.txt" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the manifest requesting administrator**

`WinTabber.Elevator/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="WinTabber.Elevator.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

- [ ] **Step 3: Declare the exact CsWin32 surface needed**

`WinTabber.Elevator/NativeMethods.txt`:

```
PostMessage
IsWindow
WM_CLOSE
```

- [ ] **Step 4: Write the program**

`WinTabber.Elevator/Program.cs`:

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

- [ ] **Step 5: Add the project to the solution**

In `WinTabber.slnx`, add a new `<Project>` line after the `WinTabber.Common.Util` entry and
before the `WinTabber.Events` entry:

```xml
  <Project Path="WinTabber.Common.Util/WinTabber.Common.Util.csproj" />
  <Project Path="WinTabber.Elevator/WinTabber.Elevator.csproj" />
  <Project Path="WinTabber.Events/WinTabber.Events.csproj" />
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` — confirms the CsWin32 source generator
resolved `PostMessage`/`IsWindow`/`WM_CLOSE` correctly with no other project's `NativeMethods.txt`
involved.

- [ ] **Step 7: Commit**

```bash
git add WinTabber.Elevator WinTabber.slnx
git commit -m "$(cat <<'EOF'
feat: add WinTabber.Elevator helper for closing elevated windows

One-shot elevated helper that posts WM_CLOSE to window handles passed as
command-line args. Its own minimal NativeMethods.txt keeps this one binary
that runs elevated free of any dependency on WinTabber.Interop.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0111j5yjjb6G2YBVrfQ6usbD
EOF
)"
```

---

### Task 2: `IWindowInterop.CloseElevatedWindows` + `InteropProxy`

**Files:**
- Modify: `WinTabber.Interop/IWindowInterop.cs`
- Modify: `WinTabber.Interop/InteropProxy.cs`
- Modify: `WinTabber.Interop/WinTabber.Interop.csproj`

**Interfaces:**
- Consumes: `WinTabber.Elevator.exe` from Task 1 (located at build/run time via
  `AppContext.BaseDirectory` — the project reference added in this task makes MSBuild copy it
  into every downstream executable's output directory, including `WinTabberUI.exe`'s).
- Produces: `IWindowInterop.CloseElevatedWindows(IEnumerable<int> handles)`, implemented on
  `InteropProxy`, for Task 3's `ApplicationRef.CloseAllWindows` to call.

No new automated test in this task: launching a real elevated process is not something this
repo's test suite exercises headlessly (same reasoning as the existing, deliberately-narrow
`WinTabber.Interop.Tests`). Build success is the verification here; the end-to-end behavior is
checked manually in Task 4.

- [ ] **Step 1: Reference the elevator project so its exe gets copied alongside the main app**

In `WinTabber.Interop/WinTabber.Interop.csproj`, add to the existing `ItemGroup` that has the
`WinTabber.Common.Util` reference:

```xml
	<ItemGroup>
	  <ProjectReference Include="..\WinTabber.Common.Util\WinTabber.Common.Util.csproj" />
	  <ProjectReference Include="..\WinTabber.Elevator\WinTabber.Elevator.csproj" />
	</ItemGroup>
```

- [ ] **Step 2: Add the interface member**

In `WinTabber.Interop/IWindowInterop.cs`, add after the existing `CloseWindow` member (just
before the interface's closing brace):

```csharp
    /// <summary>
    /// Asks the window to close (posts WM_CLOSE), the same request a click on its X button or Alt+F4
    /// sends. The window's own message loop decides whether to close immediately, prompt to save, or
    /// ignore the request. No-op if the handle is not a window.
    /// </summary>
    void CloseWindow(int handle);

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

(That final `}` is the interface's existing closing brace — this step's diff is inserting the
new member and its doc comment directly above it.)

- [ ] **Step 3: Implement it on `InteropProxy`**

In `WinTabber.Interop/InteropProxy.cs`, add immediately after the existing `CloseWindow` method:

```csharp
    public void CloseWindow(int handle)
    {
        var hwnd = new HWND(handle);
        if (handle != 0 && PInvoke.IsWindow(hwnd))
        {
            PInvoke.PostMessage(hwnd, PInvoke.WM_CLOSE, 0, 0);
        }
    }

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

`Win32Exception` is `System.ComponentModel.Win32Exception`, `ProcessStartInfo`/`Process` are
`System.Diagnostics`, and `Path`/`AppContext` resolve via this solution's global implicit usings
(`Directory.Build.props` sets `ImplicitUsings=enable`) — `InteropProxy.cs` already has `using
System.ComponentModel;` and `using System.Diagnostics;` at its top, so no new `using` lines are
needed.

- [ ] **Step 4: Build to verify**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` — confirms `InteropProxy` still fully
implements `IWindowInterop` with the new member added.

- [ ] **Step 5: Commit**

```bash
git add WinTabber.Interop/IWindowInterop.cs WinTabber.Interop/InteropProxy.cs WinTabber.Interop/WinTabber.Interop.csproj
git commit -m "$(cat <<'EOF'
feat: add IWindowInterop.CloseElevatedWindows

Launches the WinTabber.Elevator helper (added in the prior commit) elevated,
batching all handles from one close-all operation into a single UAC prompt.
Silently no-ops if elevation is declined or the helper can't be launched.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0111j5yjjb6G2YBVrfQ6usbD
EOF
)"
```

---

### Task 3: `ApplicationRef.CloseAllWindows` (TDD)

**Files:**
- Create: `WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs`
- Create: `WinTabber.Api.Windowing.Tests/ApplicationRefTests.cs`
- Modify: `WinTabber.Api.Windowing/ApplicationRef.cs`

**Interfaces:**
- Consumes: `IWindowInterop.CloseElevatedWindows(IEnumerable<int> handles)` from Task 2;
  `WindowRef.Process.IsProcessElevated` (existing, `WinTabber.Api.Windowing/WindowProcessRef.cs`);
  `WindowRef.Close()` and `WindowRef.Handle` (existing, `WinTabber.Api.Windowing/WindowRef.cs`).
- Produces: `ApplicationRef.CloseAllWindows(IEnumerable<WindowRef> windows)` — for Task 4's two
  call sites to use instead of their current hand-rolled foreach-`.Close()` loops.

- [ ] **Step 1: Write the fake `IWindowInterop`**

`WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs`:

```csharp
using System.Diagnostics;
using System.Drawing;
using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests.Fakes;

/// <summary>
/// Fake for <see cref="IWindowInterop"/> covering only what
/// <see cref="ApplicationRef.CloseAllWindows"/> exercises: elevation checks and the two close
/// paths. Every other member throws <see cref="NotSupportedException"/> — nothing in this
/// project's tests exercises the rest of this (large) interface, and a partial fake documents
/// that rather than silently no-opping.
/// </summary>
public sealed class FakeWindowInterop : IWindowInterop
{
    /// <summary>Process objects that should report as elevated. Compared by reference —
    /// <see cref="Process"/> doesn't override equality, so default <see cref="HashSet{T}"/>
    /// membership is already reference-based.</summary>
    public HashSet<Process> ElevatedProcesses { get; } = [];

    public List<int> ClosedHandles { get; } = [];
    public List<IReadOnlyList<int>> ElevatedCloseCalls { get; } = [];

    public bool IsProcessElevated(Process process) => ElevatedProcesses.Contains(process);

    public void CloseWindow(int handle) => ClosedHandles.Add(handle);

    public void CloseElevatedWindows(IEnumerable<int> handles) =>
        ElevatedCloseCalls.Add(handles.ToList());

    public void BringWindowToFront(int handle) => throw new NotSupportedException();

    public IEnumerable<int> EnumerateProcessWindowHandles(Process process) =>
        throw new NotSupportedException();

    public void ForceForeground(int hWnd) => throw new NotSupportedException();

    public Process? GetForegroundProcess() => throw new NotSupportedException();

    public Process? GetWindowProcess(int handle) => throw new NotSupportedException();

    public int GetWindowProcessId(int handle) => throw new NotSupportedException();

    public string GetWindowTitle(int hWnd) => throw new NotSupportedException();

    public void MaximizeWindow(int handle) => throw new NotSupportedException();

    public void MinimizeWindow(int handle) => throw new NotSupportedException();

    public int GetForegroundWindowHandle() => throw new NotSupportedException();

    public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare) =>
        throw new NotSupportedException();

    public void DeactivateLivePreview() => throw new NotSupportedException();

    public WindowPlacement.WindowState GetWindowState(int handle) =>
        throw new NotSupportedException();

    public WindowPlacement GetWindowPlacement(int handle) => throw new NotSupportedException();

    public void SetWindowText(int handle, string title) => throw new NotSupportedException();

    public IObservable<ActiveWindowChangeData> ActiveWindowChangedEvents() =>
        throw new NotSupportedException();

    public string GetClassName(int handle) => throw new NotSupportedException();

    public void MoveWindow(int handle, Point point) => throw new NotSupportedException();

    public bool IsTopLevel(int handle) => throw new NotSupportedException();

    public WindowStyles GetWindowStyles(int handle) => throw new NotSupportedException();

    public bool IsWindowVisible(int handle) => throw new NotSupportedException();

    public void SendInput(ushort key, bool down) => throw new NotSupportedException();

    public void MakeWindowNonActivating(nint handle) => throw new NotSupportedException();

    public bool IsWindow(int handle) => throw new NotSupportedException();

    public void HideWindow(int handle) => throw new NotSupportedException();

    public void RestoreWindow(int handle) => throw new NotSupportedException();
}
```

- [ ] **Step 2: Write the failing tests**

`WinTabber.Api.Windowing.Tests/ApplicationRefTests.cs`:

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

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj -- --treenode-filter "/*/*/ApplicationRefTests/*"`
Expected: build error / FAIL — `ApplicationRef` has no member `CloseAllWindows` yet.

- [ ] **Step 4: Implement `CloseAllWindows`**

In `WinTabber.Api.Windowing/ApplicationRef.cs`, add after the existing `NewWindowProcessRef`
method, just before the class's closing brace:

```csharp
    internal WindowProcessRef NewWindowProcessRef(Process process)
    {
        return new WindowProcessRef(process, this);
    }

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
            Manager.Interop.CloseElevatedWindows(elevatedHandles);
        }
    }
}
```

(That final `}` is the class's existing closing brace.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj -- --treenode-filter "/*/*/ApplicationRefTests/*"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add WinTabber.Api.Windowing/ApplicationRef.cs WinTabber.Api.Windowing.Tests/Fakes/FakeWindowInterop.cs WinTabber.Api.Windowing.Tests/ApplicationRefTests.cs
git commit -m "$(cat <<'EOF'
feat: add ApplicationRef.CloseAllWindows with elevation partitioning

Non-elevated windows close directly as before; elevated ones are batched
into a single IWindowInterop.CloseElevatedWindows call per invocation.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0111j5yjjb6G2YBVrfQ6usbD
EOF
)"
```

---

### Task 4: Wire up the two call sites + full verification

**Files:**
- Modify: `WinTabberUI/Coordinators/WindowCommandCoordinator.cs`
- Modify: `WinTabberUI/ViewModels/WindowSelectorViewModel.cs`

**Interfaces:**
- Consumes: `ApplicationRef.CloseAllWindows(IEnumerable<WindowRef> windows)` from Task 3.

- [ ] **Step 1: Update `WindowCommandCoordinator`**

In `WinTabberUI/Coordinators/WindowCommandCoordinator.cs`, replace the body of the
`CmdCloseApplicationWindows` case:

```csharp
                    case EventType.CmdCloseApplicationWindows:
                        // Same grouping WindowSelector uses to decide which windows belong to one
                        // app, so this closes exactly the set the switcher would show together.
                        if (!_settings.EnableCloseApplicationWindows)
                        {
                            break;
                        }
                        var currentWindow = windowManager.CurrentWindow();
                        if (currentWindow is not null)
                        {
                            var application = currentWindow.Process.Application;
                            application.CloseAllWindows(application.GetWindows());
                        }
                        break;
```

(This replaces the previous `foreach (var appWindow in currentWindow.Process.Application.GetWindows()) { appWindow.Close(); }` loop — same window set, now elevation-aware.)

- [ ] **Step 2: Update `WindowSelectorViewModel.CloseApplication`**

In `WinTabberUI/ViewModels/WindowSelectorViewModel.cs`, replace:

```csharp
    private void CloseApplication()
    {
        foreach (var item in WindowItems)
        {
            item.WindowRef.Close();
        }

        Deactivate();
    }
```

with:

```csharp
    private void CloseApplication()
    {
        if (WindowItems.Length > 0)
        {
            var application = WindowItems[0].WindowRef.Process.Application;
            application.CloseAllWindows(WindowItems.Select(item => item.WindowRef));
        }

        Deactivate();
    }
```

(Every `WindowItem` in `WindowItems` belongs to the same application — the whole switcher session
is scoped to one app's windows, per `WindowSelectorViewModel.RefreshFromForeground`/`Update` — so
reading the `ApplicationRef` off the first item is safe.)

- [ ] **Step 3: Build the full solution**

Run: `dotnet build WinTabber.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Run the full test suite for every touched project**

Run:
```bash
dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj
dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj
dotnet test WinTabber.Infrastructure.Tests/WinTabber.Infrastructure.Tests.csproj
dotnet test WinTabber.Events.Tests/WinTabber.Events.Tests.csproj
```
Expected: all four report `Passed!` with 0 failures (this also re-confirms Task 3's two new
tests still pass alongside every pre-existing test in that project).

- [ ] **Step 5: Commit**

```bash
git add WinTabberUI/Coordinators/WindowCommandCoordinator.cs WinTabberUI/ViewModels/WindowSelectorViewModel.cs
git commit -m "$(cat <<'EOF'
feat: route close-all through ApplicationRef.CloseAllWindows

Both the CmdCloseApplicationWindows shortcut and the window selector's
close-application button now go through the elevation-aware CloseAllWindows,
so elevated windows are closed via the elevator helper instead of silently
staying open.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0111j5yjjb6G2YBVrfQ6usbD
EOF
)"
```

- [ ] **Step 6: Manual verification**

This is the one behavior this plan cannot verify with an automated test (spawning a real
elevated process and clicking through a real UAC prompt isn't something a headless test suite
can do). Before considering this feature done:

1. Run `dotnet run --project WinTabberUI/WinTabberUI.csproj`.
2. Open a normal, non-elevated application with multiple windows (e.g. two Notepad windows).
   Trigger close-all on it (chrome button or the `CmdCloseApplicationWindows` shortcut,
   `Ctrl+Alt+Shift+W` by default). Expect: both windows close immediately, no UAC prompt —
   unchanged from before this plan.
3. Run an application elevated (right-click → "Run as administrator" — Task Manager or Notepad
   both work) so it has at least one elevated window. Trigger close-all on it. Expect: exactly
   one UAC prompt appears, and on accepting it, the elevated window closes.
4. Repeat step 3 but decline the UAC prompt. Expect: the elevated window stays open, nothing
   crashes, no error dialog.
5. Open an elevated application with two windows (or two separately-elevated instances grouped
   as one application) and trigger close-all. Expect: only **one** UAC prompt for the whole
   operation, not two.
