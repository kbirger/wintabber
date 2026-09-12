# Elevation backend abstraction: gsudo support + Focus Select elevation gap

## Context

The elevated-window close-all feature (`docs/superpowers/specs/2026-09-11-elevated-window-close-design.md`)
added a one-shot elevated helper, `WinTabber.Elevator`, launched via `ShellExecute`/`runas` whenever
`ApplicationRef.CloseAllWindows` finds an elevated window it can't `WM_CLOSE` directly (Windows'
UIPI blocks that across integrity levels). That design deliberately chose a one-shot launch over a
persistent broker, accepting a UAC prompt on every such close-all as the cost of a smaller attack
surface.

Two things prompted this follow-up:

1. **gsudo** (a third-party "sudo for Windows" tool) has a credentials cache that can eliminate
   repeat UAC prompts for a whole app session, at the cost of reopening — via a third party's
   implementation rather than one of our own — the same "persistent elevated capability" trade-off
   the original design chose against. The user wants this offered as a configurable, opt-in
   alternative backend, not a replacement for the built-in one-shot elevator.
2. **Focus Select has the identical, previously-unnoticed gap.** `WindowSelectorViewModel.MinimizeOthers`
   calls `WindowRef.Minimize()` directly with no elevation awareness at all — `ShowWindow`/`SetWindowPos`
   are blocked by UIPI the same way `WM_CLOSE` and `MoveWindow` are (recall `WindowRef.MoveTo` already
   skips elevated windows for exactly this reason). Focus Select silently fails to minimize elevated
   windows today. Fixing this properly requires the same elevation-bridging capability close-all has,
   generalized to more than one action (close vs. minimize).

## Decisions made during brainstorming

- **gsudo's cache is not "free."** Per gsudo's own documentation, the default cache mode
  (`CacheMode explicit`) means a bare `gsudo <command>` shows its own UAC prompt every time, with
  zero benefit over what exists today — the benefit only appears if something explicitly runs
  `gsudo cache on` first. That call itself costs one UAC prompt and starts a session that "allows
  elevation from one invoker process and its children" (the invoker being whichever process ran
  `gsudo cache on`) until the invoker exits or `CacheDuration` (default 5 minutes, resettable on
  each use) elapses. gsudo's own docs warn: "a malicious process could trick the allowed process...
  and force a running gsudo cache instance to elevate silently." **Decision: pursue gsudo anyway,
  with its cache enabled by default when the user selects it as the backend, using a short,
  non-default cache duration (30 seconds) rather than gsudo's 5-minute default, to bound the
  exposure window.** This is an explicit, informed acceptance of that trade-off, opt-in per user
  (default backend remains the built-in one-shot elevator), not a silent behavior change.
- **Two elevation backends, chosen via settings, behind one abstraction.** Neither backend
  replaces the other; `GeneralSettings.ElevationBackend` picks between them, defaulting to the
  existing built-in elevator.
- **Offer to auto-install gsudo via `winget install --id gerardog.gsudo`** (the `--id` is required —
  a bare `winget install gsudo` resolves to an unrelated Microsoft Store listing) when the user
  selects the gsudo backend and it isn't detected on PATH, rather than only linking out to manual
  install instructions.
- **Generalize the elevator and centralize the partition logic**, rather than bolting a second,
  parallel "minimize-elevated" path onto `WindowSelectorViewModel` — the elevation-aware
  partition-and-dispatch logic that `ApplicationRef.CloseAllWindows` already has is extracted onto
  `WindowManager` so both close-all and Focus Select's minimize-others (in both its scope modes)
  share one implementation.
- **The new selection/fallback logic stays testable.** Rather than have the resolver depend on the
  concrete `GeneralSettings` type (which would need `WinTabber.Interop` to reference
  `WinTabber.Infrastructure`, a new and unnecessary cross-project dependency), it depends on a
  small `IElevationBackendProvider` interface defined alongside it in `WinTabber.Interop`. A trivial
  adapter in `WinTabberUI` wraps `GeneralSettings.ElevationBackend` to satisfy it. This keeps the
  entire abstraction — enums, interfaces, both launchers, and the resolver — inside
  `WinTabber.Interop`, unit-testable in `WinTabber.Interop.Tests` with fakes for everything.
- **Open verification item, not yet confirmed from documentation alone:** the exact unit and
  reliability of the `-d` duration flag on `gsudo cache on` (seconds vs. another unit). To be
  confirmed empirically during implementation/manual testing, not assumed.

## Architecture

`IElevationLauncher` (new interface, `WinTabber.Interop`) abstracts "run this action, on these
window handles, elevated" behind two implementations: `BuiltInElevationLauncher` (today's
`ShellExecute`/`runas` launch of `WinTabber.Elevator.exe`, generalized to take an action verb) and
`GsudoElevationLauncher` (new: launches the same elevator via `gsudo.exe`, lazily establishing a
short-lived credentials-cache session so repeat elevations within a session don't re-prompt).
`ElevationLauncherResolver` picks between them based on `GeneralSettings.ElevationBackend`
(surfaced through a small provider interface, not a direct settings dependency), falling back to
`BuiltIn` if `Gsudo` is selected but not actually available. `WinTabber.Elevator` itself is
generalized from "always close" to "close or minimize, per an argument," so both backends can
route either action through the same helper binary. The elevation-partition-and-batch logic
(elevated handles batched into one launch; non-elevated ones handled directly) moves from
`ApplicationRef.CloseAllWindows` onto `WindowManager`, so Focus Select's `MinimizeOthers` can reuse
it for minimize the same way close-all reuses it for close.

## Components

### `WinTabber.Interop/ElevatedWindowAction.cs` (new)

```csharp
namespace WinTabber.Interop;

public enum ElevatedWindowAction
{
    Close,
    Minimize,
}
```

### `WinTabber.Interop/ElevationBackend.cs` (new)

```csharp
namespace WinTabber.Interop;

public enum ElevationBackend
{
    BuiltIn,
    Gsudo,
}
```

Lives in `WinTabber.Interop`, not `WinTabber.Infrastructure`, purely so `ElevationLauncherResolver`
(below) never needs a reference to `WinTabber.Infrastructure`. `GeneralSettings.ElevationBackend`
uses this type directly — the same pattern `GeneralSettings.FocusSelectModifier` already
establishes by using `WinTabber.Events.Shortcuts.ShortcutModifiers`, an enum from a different,
lower-layer project.

### `WinTabber.Interop/IElevationLauncher.cs` (new)

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

### `WinTabber.Interop/BuiltInElevationLauncher.cs` (new — logic moved out of `InteropProxy`)

Today's `CloseElevatedWindows` body, generalized: resolves `WinTabber.Elevator.exe` via
`AppContext.BaseDirectory`, launches it with `UseShellExecute=true, Verb="runas"`, and now passes
the action as the first argument (`"close"` or `"minimize"`, lowercased) followed by the handles.
`IsAvailable => true` always — it's our own bundled binary. Same `try/catch (Win32Exception)`
silent-failure behavior as today.

### `WinTabber.Interop/GsudoElevationLauncher.cs` (new)

```csharp
public class GsudoElevationLauncher : IElevationLauncher
{
    private bool _cacheStarted;

    public bool IsAvailable => TryResolveGsudoPath() is not null;

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)
    {
        var gsudoPath = TryResolveGsudoPath();
        if (gsudoPath is null)
        {
            return;
        }

        EnsureCacheStarted(gsudoPath);

        var elevatorPath = Path.Combine(AppContext.BaseDirectory, "WinTabber.Elevator.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = gsudoPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(elevatorPath);
        startInfo.ArgumentList.Add(action.ToString().ToLowerInvariant());
        foreach (var handle in handles)
        {
            startInfo.ArgumentList.Add(handle.ToString());
        }

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // gsudo declined, disappeared between the availability check and this call, or
            // failed to launch for some other reason. Same silent end state as the built-in path.
        }
    }

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
            // Couldn't start the cache session (e.g. declined) — leave _cacheStarted false so
            // the next call tries again; the subsequent RunElevated call below will still show
            // its own UAC prompt via gsudo's normal (uncached) elevation.
        }
    }

    private static string? TryResolveGsudoPath()
    {
        // PATH probe — gsudo's own install docs confirm it does this "except adding gsudo to the
        // PATH", so a PATH-based resolution is the documented detection mechanism, not a guess.
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
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

(Illustrative — exact structure to be finalized during implementation, particularly whether
`EnsureCacheStarted` should block on `WaitForExit()` before the first real action, which the sketch
above does to avoid a race between "cache session starting" and "the real elevated call" landing
before the cache is ready.)

### `WinTabber.Elevator/Program.cs` (generalized)

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

`NativeMethods.txt` gains `ShowWindow` and `SW_MINIMIZE` alongside the existing `PostMessage`,
`IsWindow`, `WM_CLOSE`. Still zero dependency on `WinTabber.Interop`.

### `IWindowInterop` / `InteropProxy`

`CloseElevatedWindows(IEnumerable<int> handles)` is replaced by:

```csharp
void RunElevatedAction(ElevatedWindowAction action, IEnumerable<int> handles);
```

`InteropProxy` implements it by delegating to an injected `IElevationLauncher` (guarding the same
empty-collection early-return the current implementation has).

### `WinTabber.Interop/IElevationBackendProvider.cs` (new)

```csharp
namespace WinTabber.Interop;

public interface IElevationBackendProvider
{
    ElevationBackend Backend { get; }
}
```

### `WinTabber.Interop/ElevationLauncherResolver.cs` (new)

```csharp
public class ElevationLauncherResolver : IElevationLauncher
{
    private readonly BuiltInElevationLauncher _builtIn;
    private readonly GsudoElevationLauncher _gsudo;
    private readonly IElevationBackendProvider _backendProvider;

    public ElevationLauncherResolver(
        BuiltInElevationLauncher builtIn,
        GsudoElevationLauncher gsudo,
        IElevationBackendProvider backendProvider)
    {
        _builtIn = builtIn;
        _gsudo = gsudo;
        _backendProvider = backendProvider;
    }

    private IElevationLauncher Active =>
        _backendProvider.Backend == ElevationBackend.Gsudo && _gsudo.IsAvailable ? _gsudo : _builtIn;

    public bool IsAvailable => true; // the resolver itself always has a usable backend (falls back to BuiltIn)

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles) =>
        Active.RunElevated(action, handles);
}
```

### `WinTabberUI` wiring

- A trivial `GeneralSettingsElevationBackendProvider : IElevationBackendProvider` wrapping
  `GeneralSettings.ElevationBackend` (a few lines, in `WinTabberUI/Services/` or similar).
- `Bootstrapper.cs` registers `BuiltInElevationLauncher`, `GsudoElevationLauncher`, the provider
  adapter, and `IElevationLauncher → ElevationLauncherResolver`; `InteropProxy` gains an
  `IElevationLauncher` constructor parameter. **`GsudoElevationLauncher` must be registered as a
  singleton** (`AddSingleton`, matching how `InteropProxy` itself and most other services in this
  bootstrapper are registered) — its `_cacheStarted` flag is only meaningful if the same instance
  persists for the app's lifetime; a transient registration would silently defeat the cache
  entirely, re-running `gsudo cache on` (and re-prompting) on every single elevated action.

### Settings (`GeneralSettings.ElevationBackend`, new field, default `ElevationBackend.BuiltIn`)

Settings UI: a combo box for the backend choice. When `Gsudo` is selected and
`GsudoElevationLauncher.IsAvailable` is false, an inline card appears: "gsudo not found" with an
**Install** button. That button runs `winget install --id gerardog.gsudo` (via `Process.Start`,
`UseShellExecute=false`, capturing/ignoring output — winget's own UI, if any, is whatever winget
itself does for that package; this may itself prompt UAC once, which is fine — it's a one-time,
user-initiated action, not the app's own silent runtime elevation) and re-runs the availability
check afterward to update the card.

### `WinTabber.Api.Windowing/WindowManager.cs` (new methods)

```csharp
public void CloseWindows(IEnumerable<WindowRef> windows) => PerformAction(windows, ElevatedWindowAction.Close);

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
```

### `ApplicationRef.CloseAllWindows` (simplified to a thin wrapper)

```csharp
public void CloseAllWindows(IEnumerable<WindowRef> windows)
{
    var windowList = windows as IReadOnlyCollection<WindowRef> ?? windows.ToList();
    foreach (var window in windowList)
    {
        AssertOwnsWindow(window);
    }

    Manager.CloseWindows(windowList);
}
```

(`AssertOwnsWindow` — the ownership check added in the prior spec's final review — stays here,
since "does this application own these windows" is meaningful only at the `ApplicationRef` level,
not at `WindowManager`'s system-wide level.)

### `WindowSelectorViewModel.MinimizeOthers`

Both branches replace their direct `.Minimize()` loops with a single call:

```csharp
private void MinimizeOthers(WindowItem selected)
{
    if (_settings.FocusSelectScope == FocusSelectScope.AllWindows)
    {
        WindowManager.MinimizeWindows(
            WindowManager.GetWindows().Where(window => window.Handle != selected.Handle));
        return;
    }

    WindowManager.MinimizeWindows(
        WindowItems.Where(item => item != selected).Select(item => item.WindowRef));
}
```

No `AssertOwnsWindow`-style check here — unlike `CloseAllWindows`'s public API contract (called
from outside this class), `MinimizeOthers`'s window sets are already internally consistent
(`WindowItems`/`WindowManager.GetWindows()`), so an ownership assertion would add nothing.

## Data flow

**Close-all** (unchanged shape from the prior spec, now routed through the shared method): trigger
→ `ApplicationRef.CloseAllWindows` → ownership check → `WindowManager.CloseWindows` → partition →
non-elevated close directly, elevated batched into one `IElevationLauncher.RunElevated(Close, …)`
call → resolver picks `BuiltIn` or `Gsudo` per settings/availability → elevator (however launched)
posts `WM_CLOSE` to each handle.

**Focus Select minimize** (newly elevation-aware): selection committed with the configured modifier
held → `MinimizeOthers` → `WindowManager.MinimizeWindows` on either the switcher's tile set or every
window system-wide → same partition → non-elevated windows minimize directly, elevated ones batched
into one `RunElevated(Minimize, …)` call → same resolver/backend path as close-all.

## Error handling

| Failure | Handling |
|---|---|
| gsudo's cache-establishing prompt declined | `EnsureCacheStarted` catches it, `_cacheStarted` stays `false`; the very next `RunElevated` call still runs (uncached — gsudo shows its own prompt for that call). No crash, no repeated cache-start attempts within the same call. |
| gsudo selected but disappears between availability check and use | `RunElevated`'s own `try/catch (Win32Exception)` — same silent end state as today's built-in path. |
| `winget install` fails (winget missing, network failure, user cancels) | Settings UI shows the failure inline (not a crash); availability re-check afterward correctly still shows "not found." |
| Elevated window closed/moved between enumeration and the elevator running | Unchanged from the prior spec — the elevator's own `IsWindow` check skips it. |

## Testing

- `ElevationLauncherResolver`: new tests in `WinTabber.Interop.Tests` — `BuiltIn` selected → always
  uses `BuiltInElevationLauncher`; `Gsudo` selected + available → uses `GsudoElevationLauncher`;
  `Gsudo` selected + *not* available → falls back to `BuiltInElevationLauncher`. Needs fakes for
  `IElevationLauncher` (two instances, tracking calls) and `IElevationBackendProvider`.
- `WindowManager.CloseWindows`/`MinimizeWindows`: extend the existing partition tests (currently
  `ApplicationRefTests.cs`, testing through `ApplicationRef.CloseAllWindows`) to also cover
  `MinimizeWindows` directly, using the existing `FakeWindowInterop` (renaming its
  `CloseElevatedWindows` tracking to match the new `RunElevatedAction` signature, tracking the
  action alongside the handles).
- `BuiltInElevationLauncher`, `GsudoElevationLauncher`, the elevator's verb dispatch, and the
  winget install shell-out remain untested per this repo's established precedent (thin Win32/
  process-launch glue) — manual verification covers all of these, expanded from the prior spec's
  checklist to include: gsudo backend close-all (first call prompts once, a second within 30s
  doesn't, one after 30s prompts again), gsudo backend Focus Select minimize (same pattern), and
  the winget install button's happy and unhappy paths.

## Alternatives considered and rejected

- **gsudo as pure pass-through, no cache.** Would deliver literally none of the motivation for
  choosing gsudo over the built-in elevator (same one-prompt-per-action behavior either way) —
  rejected in favor of enabling the cache with a tightened duration.
- **A separate, second toggle for "enable gsudo cache" independent of backend choice.** Rejected
  as unnecessary indirection — the whole point of picking `Gsudo` as the backend *is* the cache;
  splitting it into two settings only adds a state (`Gsudo` backend, cache off) that provides no
  benefit over `BuiltIn` while still carrying gsudo as a dependency.
- **Detect-only, link out to manual gsudo install.** Simpler and smaller blast radius, but the
  user explicitly asked for the auto-install offer via winget.
