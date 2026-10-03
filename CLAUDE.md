# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build the solution
dotnet build WinTabber.slnx

# Run the main WPF application
dotnet run --project WinTabberUI/WinTabberUI.csproj

# Run all tests (the --solution flag is required on the .NET 10 SDK)
dotnet test --solution WinTabber.slnx

# Run specific test projects
dotnet test WinTabber.Infrastructure.Tests/WinTabber.Infrastructure.Tests.csproj
dotnet test WinTabber.Events.Tests/WinTabber.Events.Tests.csproj
dotnet test WinTabber.Api.Windowing.Tests/WinTabber.Api.Windowing.Tests.csproj
dotnet test WinTabber.Api.Media.Tests/WinTabber.Api.Media.Tests.csproj
dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj

# Run specific test class (this repo's runner is Microsoft.Testing.Platform, not VSTest —
# --filter does not work; use --treenode-filter instead)
dotnet test WinTabber.Infrastructure.Tests -- --treenode-filter "/*/*/TrieNodeTests/*"

# Run the manual session test console app (currently disabled — see Testing section)
dotnet run --project Wintabber.SessionsTest/Wintabber.SessionsTest.csproj

# Watch mode
dotnet watch run --project WinTabberUI/WinTabberUI.csproj
```

Code formatting uses CSharpier (120 char line width, 4-space indentation — configured in `.csharpierrc.yaml`).

## Architecture Overview

WinTabber is a Windows desktop application for window switching and media/audio control. It is a WPF app targeting .NET 10 / Windows 10.0.26100+, built with MVVM (ReactiveUI + CommunityToolkit.MVVM), DI (Microsoft.Extensions.DependencyInjection), and reactive streams (System.Reactive, DynamicData).

### Project Layers

```
WinTabberUI            ← WPF app, MVVM ViewModels, DI bootstrap, window management
  WinTabber.Api.Windowing ← Window registry (WindowManager, ApplicationRef, WindowRef),
                            process suspension, DWM thumbnail service
  WinTabber.Api.Media  ← Audio (WASAPI/NAudio), SMTC, shell app discovery
  WinTabber.Events     ← Global keyboard/mouse input (SharpHook), event dispatch, HyperKey
  WinTabber.Interop    ← Windows API abstraction (IProcessControl, IWindowVisibility,
                          IWindowPlacement, IWindowInterop / InteropProxy via CsWin32)
  WinTabber.Infrastructure ← Settings model + persistence, app icon/AUMID cache, hint trie/radix trie
  WinTabber.UI.Common  ← Shared XAML themes, converters, behaviors, hint system
  WinTabber.UI.Media   ← Media controls views and their WPF-specific viewmodels (framework-free
                          media viewmodels and services now live in WinTabber.ViewModels)
  WinTabber.Common.Util← Extension methods (Observable, Process, Debug, Object)
  WinTabber.Generators ← Roslyn source generator: [Lazy] attribute → lazy init code
  WinTabber.ViewModels ← App ViewModels and framework-free services (e.g. MediaControlsStateService,
                          MediaSessionService), no WPF/WinUI dependency; references Api.Media,
                          Api.Windowing, Events, Infrastructure, Interop, Common.Util;
                          consumed by both WinTabberUI and winui3/WinTabberUI
```

`WinTabber.Api.*` is a flat family of UI-less capability layers, not a hierarchy —
`Api.Windowing` and `Api.Media` are siblings with no reference in either direction. A new
capability layer with no WPF dependency belongs here; anything that needs WPF does not.

### winui3/ — in-progress WinUI 3 migration

`winui3/` holds a second, independent desktop app (`winui3/WinTabberUI`, `winui3/WinTabber.UI.Common`,
`winui3/WinTabber.UI.Media` + `.Tests` siblings) that mirrors the WPF app's project names but targets
Windows App SDK / WinUI 3 instead. It is currently just a bootable shell with no real UI — Phase 2+ of
an ongoing migration (see `docs/superpowers/plans/2026-09-12-wpf-to-winui3-migration.md`). The WPF app
is not being retired by this migration; both apps build and run side by side, sharing the
UI-framework-agnostic projects above, including the new `WinTabber.ViewModels`.

### Key Patterns

**DI Bootstrap** — `WinTabberUI/Bootstrapper.cs` registers all services, repos, viewmodels, and windows. This is the single place to wire new dependencies.

**Windows Interop** — The boundary is *what the call acts on*, not which layer you happen to be in:

- Win32 that **observes or mutates another process's windows or processes** (enumeration,
  activation, placement, suspend/resume, elevation) goes through four interfaces in
  `WinTabber.Interop`, split so a consumer only depends on the surface it needs:
  `IProcessControl` (suspend/resume, image path, debug privilege), `IWindowVisibility`
  (hide/restore — the minimal surface process suspension needs), `IWindowPlacement` (off-screen
  move/restore, resize, taskbar-button visibility, used for thumbnailing), and `IWindowInterop`
  (the remaining window surface — activation, enumeration, styles, live preview, and more —
  which extends `IWindowVisibility`). The concrete `InteropProxy` implements `IProcessControl`,
  `IWindowPlacement`, and `IWindowInterop` (covering `IWindowVisibility` too) using CsWin32
  bindings from `WinTabber.Interop/NativeMethods.txt`. Do not call these directly from other
  projects — the interfaces are the seam `WinTabber.Api.Windowing.Tests/Fakes/FakeProcessControl.cs`
  (which implements just `IProcessControl` + `IWindowVisibility`, the pair
  `ProcessSuspensionService` depends on) fakes.
- Win32 that **affects the rendering of our own windows** through **CsWin32-backed** APIs (DWM
  composition, corner preference, cloak/peek, thumbnails, hit-test and resize messages) lives with
  the WPF code that owns the `HwndSource` — `WinTabber.UI.Common/Chrome/`, `WinTabberUI`, and
  `WinTabber.UI.Media` (which converts a shell icon's `HBITMAP` into a WPF `ImageSource` for its
  own display), each with its own `NativeMethods.txt` (e.g. `DwmSetWindowAttribute`/
  `DWM_WINDOW_CORNER_PREFERENCE` in `WinTabber.UI.Common/NativeMethods.txt`). It is not routed
  through the interfaces above: the surrounding code is WPF and untestable headlessly, so the seam
  would buy nothing.
- `WinTabber.Api.Media` owns its Shell/AUMID bindings for the same reason.

The own-window-rendering carve-out above applies only to **CsWin32-backed** Win32. A **hand-written**
`[DllImport]` for an undocumented API — one CsWin32 has no metadata for — always lives in
`WinTabber.Interop`, regardless of what it acts on: `NtNativeMethods.cs`
(`NtSuspendProcess`/`NtResumeProcess`), `PInvoke.cs`'s `DwmpActivateLivePreview`, and
`ChromeInterop.cs`'s `SetWindowCompositionAttribute` are all examples — the last of these acts on
our own window's chrome (consumed by `WinTabber.UI.Common/Chrome/Interop.cs` for blur/accent) but
still lives in `WinTabber.Interop` because it's hand-written, not CsWin32-generated. Never add an
entry to a `NativeMethods.txt` without a call site — every one of these files had accumulated dead
surface, and CsWin32 generates dependent types transitively, so listing a type explicitly is
usually unnecessary.

**Event Flow** — `WinTabberEventManager` (Events project) broadcasts `EventType` commands triggered by global hotkeys from `InputListenerService`. UI layers subscribe to these observables.

**Window Model** — `WindowManager` maintains a live registry of open windows as `WindowRef`/`ApplicationRef`. `CircularBuffer` tracks activation history for Alt-Tab-style switching.

**Reactive State** — App state flows through `ApplicationState` and service classes (`ActiveWindowStateService`, `MediaControlsStateService`) as `IObservable<T>`. ViewModels subscribe and expose reactive properties.

**Audio/Media** — `CoreAudioSessionRepository` (WASAPI) enumerates audio sessions; `SMTCSessionRepository` wraps System Media Transport Controls. Both are consumed via services in `WinTabber.Api.Media`.

### Testing

All test projects use TUnit. The `test` runner opt-in in `global.json` is required — without it `dotnet test`
fails outright on the .NET 10 SDK, which no longer supports the VSTest target.

- `WinTabber.Events.Tests` — TUnit; shortcut model tests (trigger matching, conflict detection, commit tracking)
- `WinTabber.Infrastructure.Tests` — TUnit; contains `TrieNodeTests`, settings persistence, and infrastructure-level tests. References `WinTabber.Infrastructure` directly (not `WinTabberUI`); no retry policy needed since it no longer drags in the WPF app.
- `WinTabber.Api.Windowing.Tests` — TUnit; process-suspension and suspended-window-store tests, using `Fakes/FakeProcessControl.cs`
- `WinTabber.Api.Media.Tests` — TUnit; deliberately narrow — covers `CoreAudioDeviceRepository`'s null-endpoint path and monitor callback wiring via `Fakes/FakeMMDeviceEnumeratorWrapper.cs`; see the project's own README.md for what's covered and why
- `WinTabber.Interop.Tests` — TUnit; deliberately narrow — covers `ProcessHelper.IsSystemProcess`/`ClassifyNonSystemProcesses`, the only pure logic in the project not requiring a real Win32 call; see the project's own README.md for what's covered and why
- `Wintabber.SessionsTest` — Console app for manual session/audio testing (not a test framework); currently disabled (`Program.cs` is a single placeholder line, no `WinTabberUI`/`WinTabber.Api.Windowing` references)
- `winui3/WinTabber.UI.Common.Tests`, `winui3/WinTabber.UI.Media.Tests` — TUnit; currently carry only a placeholder test each, to keep the empty scaffold projects passing CI, pending Phase 2 filling them in with real coverage
