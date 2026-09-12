# Elevated-window close-all design

## Context

The close-application-windows feature (button on the window selector's chrome, and the
`CmdCloseApplicationWindows` shortcut) closes every window of an application via `WM_CLOSE`
(`WindowRef.Close()` → `IWindowInterop.CloseWindow` → `PostMessage`). This silently fails for
windows owned by an elevated (admin) process: Windows' UIPI (User Interface Privilege Isolation)
blocks most window messages, including `WM_CLOSE`, from a lower-integrity sender to a
higher-integrity window.

This is not a new problem in this codebase. `WindowRef.MoveTo()` already special-cases it (skips
the move entirely if `Process.IsProcessElevated`), and `ProcessSuspensionService.CanSuspend`
already refuses to suspend elevated processes. Both of those chose to simply not attempt the
action rather than bridge the privilege gap.

For close-all, the decision (made explicitly, after presenting both options) is to actually close
elevated windows rather than continue that skip-only precedent, using a small elevated helper
process.

## Decisions made during brainstorming

- **Bridge the gap, don't just skip** (rejected the lower-risk "detect elevated, skip" option that
  would have matched `MoveTo`/`CanSuspend`).
- **One-shot helper per invocation**, not a persistent elevated broker. A long-lived admin-level
  background process is a meaningfully bigger, harder-to-reason-about attack surface than a
  process that exists for a fraction of a second and exits. The cost is a UAC prompt every time
  this scenario is hit (i.e. only when the application being closed actually has an elevated
  window — most applications don't).
- **Silent failure on UAC decline or launch failure.** Matches the existing skip-only precedent's
  end state: the elevated windows simply stay open, exactly as if this feature didn't exist. No
  new notification mechanism.
- **Batch handles into a single elevated launch per close-all operation**, so an application with
  several elevated windows triggers one UAC prompt, not one per window.

## Architecture

A new minimal helper project, `WinTabber.Elevator`, launched on demand (never at app startup,
never unless a close-all operation actually needs it) whenever `ApplicationRef.CloseAllWindows()`
finds at least one elevated window in the set being closed. It receives the target window handles
as command-line arguments, posts `WM_CLOSE` to each from an elevated context — so UIPI no longer
blocks it, since sender and receiver are now equal-or-higher integrity — and exits immediately.

No IPC channel, no persistent state, no communication back to the main app (fire-and-forget,
matching the existing non-elevated `CloseWindow`'s fire-and-forget style).

## Components

### `WinTabber.Elevator` (new project)

- `OutputType=WinExe` (Windows subsystem, not console) even though it has no window: a console
  (`Exe`) subsystem binary launched via `ShellExecute`/`runas` gets a visible console window that
  flashes briefly before the process exits, since `CreateNoWindow` only suppresses that when
  `UseShellExecute=false` — which the `runas` verb requires to be `true`. `WinExe` avoids any
  visible flash without needing a window, message loop, or WPF/WinForms dependency.
- Own `app.manifest` with `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />`
  (mirrors the structure of `WinTabberUI/app.manifest`, which requests `asInvoker`).
- Own minimal `NativeMethods.txt` containing only `PostMessage` and `IsWindow`, generated via
  CsWin32 — **does not** reference `WinTabber.Interop`. Keeping the admin-level binary's own
  dependency surface as small as possible matters more here than reusing `InteropProxy`'s
  existing bindings: this is the one binary in the solution that runs elevated, so it should have
  the least code in it, not the most.
- `Program.cs`: for each command-line argument, parse it as an `int` window handle; if
  `PInvoke.IsWindow` reports it as still valid, call `PInvoke.PostMessage(hwnd, WM_CLOSE, 0, 0)`.
  Skip anything that fails to parse or is no longer a window (e.g. the user closed it in the
  interval between the check and this process actually starting). No output, no logging, exit 0
  unconditionally — there's nothing upstream listening for a result.

### `ApplicationRef.CloseAllWindows()` (new method, `WinTabber.Api.Windowing`)

Replaces the duplicated foreach-`.Close()` loops in `WindowCommandCoordinator`'s
`CmdCloseApplicationWindows` case and `WindowSelectorViewModel.CloseApplication()` — both call
sites already have an `ApplicationRef`/window set from the same "one application's windows" source
and were independently looping and calling `WindowRef.Close()`; centralizing avoids the elevation
partitioning logic being written twice.

```csharp
public void CloseAllWindows()
{
    var windows = GetWindows();
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
```

(Exact signature/placement to be finalized during implementation — this illustrates the shape,
not final code.)

### `IWindowInterop.CloseElevatedWindows(IEnumerable<int> handles)` (new interop method)

`InteropProxy`'s implementation:

- Resolves the elevator's path via `Path.Combine(AppContext.BaseDirectory, "WinTabber.Elevator.exe")`
  (same output directory as the main executable; this app has no single-file/self-contained
  publish step today, so no special packaging is needed for a second exe to land alongside it).
- `Process.Start(new ProcessStartInfo { FileName = elevatorPath, UseShellExecute = true, Verb = "runas", ArgumentList = { ...handles.Select(h => h.ToString()) } })`.
- Does not call `WaitForExit()` — fire-and-forget, consistent with the non-elevated `CloseWindow`.
- Wraps the `Process.Start` call in a `try/catch` for `Win32Exception` (thrown when the user
  declines the UAC prompt, `ERROR_CANCELLED`) and any other launch failure (e.g. the elevator
  binary is missing from a broken install) and swallows it — see Error handling below.

## Data flow

1. Close-all triggered (chrome button or `CmdCloseApplicationWindows` shortcut).
2. `ApplicationRef.CloseAllWindows()` partitions the application's windows.
3. Non-elevated windows close immediately via the existing `WM_CLOSE` path — unchanged behavior.
4. If any elevated windows exist, **one** `WinTabber.Elevator.exe <handle1> <handle2> ...` is
   launched elevated.
5. Windows shows one UAC consent prompt for that launch.
6. On consent, the elevator posts `WM_CLOSE` to each handle (now succeeding) and exits.
7. On decline, or if the elevator can't be launched at all, nothing further happens — those
   windows remain open.

## Error handling

| Failure | Handling |
|---|---|
| UAC prompt declined | `Process.Start` throws `Win32Exception` (`ERROR_CANCELLED`); caught and swallowed in `InteropProxy.CloseElevatedWindows`. Elevated windows stay open. |
| Elevator binary missing/broken | Same catch (`Win32Exception`/`FileNotFoundException`); same silent outcome. |
| A targeted window closed between the check and the elevator running | The elevator's own `IsWindow` check skips it; not an error, nothing to report. |
| A targeted handle fails to parse (should not happen — we control the caller) | Skipped by the elevator; not an error. |

No user-facing notification in any of these cases (see "Decisions made during brainstorming").

## Testing

- `WinTabber.Elevator` itself is thin Win32-calling glue with no branching logic worth unit-testing
  headlessly — same rationale documented for `WinTabber.Interop.Tests`' narrow scope (e.g.
  `MediaKeySender`).
- `ApplicationRef.CloseAllWindows()`'s partitioning (elevated → batched interop call, non-elevated
  → direct `Close()`) is the one new piece of *pure* logic and is testable in
  `WinTabber.Api.Windowing.Tests`, which already fakes elevation status
  (`Fakes/FakeProcessControl.cs`) for the suspension feature. A new test should assert: all
  non-elevated windows get `Close()` called directly, and `CloseElevatedWindows` is called exactly
  once with exactly the elevated handles (not once per elevated window).
- No end-to-end test of the actual UAC flow — that's not something this repo's test suite can
  exercise headlessly, and manual verification (triggering close-all against a window elevated on
  purpose, e.g. Task Manager run as admin) is the appropriate check before calling this done.

## Alternatives considered and rejected

- **Detect + skip (matching `MoveTo`/`CanSuspend`).** Simpler, zero new attack surface, no UAC
  prompts — but explicitly rejected in favor of actually closing elevated windows.
- **Persistent elevated broker process with a named-pipe IPC channel.** Would avoid repeat UAC
  prompts across multiple close-all actions in one session, but a long-lived admin-level background
  process is a materially bigger security surface to build, reason about, and keep patched than a
  helper that exists for a fraction of a second. Rejected in favor of the one-shot approach.
- **Windows 11's inbox `sudo` command** (available on this app's target build, 26100+) instead of
  the classic `ShellExecute`/`runas` verb. Not used: `sudo` is an opt-in feature under Windows
  Developer Settings, off by default, and its interactive modes (new console window, or input
  disabled) are designed for terminal use, not a silent background action. `ShellExecute` with
  `Verb="runas"` works unconditionally regardless of that setting and needs no console.
