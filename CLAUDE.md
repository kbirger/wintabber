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
dotnet test WinTabber.Api.Tests/WinTabber.Api.Tests.csproj

# Run specific test class
dotnet test WinTabber.Infrastructure.Tests --filter TrieNodeTests

# Run the manual session test console app
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
  WinTabber.API        ← Core window registry (WindowManager, ApplicationRef, WindowRef)
  WinTabber.Events     ← Global keyboard/mouse input (SharpHook), event dispatch, HyperKey
  WinTabber.Interop    ← Windows API abstraction (IInteropProxy / InteropProxy via CsWin32)
  WinTabber.Api.Media  ← Audio (WASAPI/NAudio), SMTC, shell app discovery
  WinTabber.UI.Common  ← Shared XAML themes, converters, behaviors, hint system
  WinTabber.UI.Media   ← Media controls views and viewmodels
  WinTabber.Common.Util← Extension methods (Observable, Process, Debug, Object)
  WinTabber.Generators ← Roslyn source generator: [Lazy] attribute → lazy init code
```

### Key Patterns

**DI Bootstrap** — `WinTabberUI/Bootstrapper.cs` registers all services, repos, viewmodels, and windows. This is the single place to wire new dependencies.

**Windows Interop** — All Win32 calls go through `IInteropProxy` (defined in `WinTabber.Interop/IInteropProxy.cs`). The concrete `InteropProxy` uses CsWin32-generated bindings from `NativeMethods.txt`. Never call P/Invoke directly from other projects.

**Event Flow** — `WinTabberEventManager` (Events project) broadcasts `EventType` commands triggered by global hotkeys from `InputListenerService`. UI layers subscribe to these observables.

**Window Model** — `WindowManager` maintains a live registry of open windows as `WindowRef`/`ApplicationRef`. `CircularBuffer` tracks activation history for Alt-Tab-style switching.

**Reactive State** — App state flows through `ApplicationState` and service classes (`ActiveWindowStateService`, `MediaControlsStateService`) as `IObservable<T>`. ViewModels subscribe and expose reactive properties.

**Audio/Media** — `CoreAudioSessionRepository` (WASAPI) enumerates audio sessions; `SMTCSessionRepository` wraps System Media Transport Controls. Both are consumed via services in `WinTabber.Api.Media`.

### Testing

All test projects use TUnit. The `test` runner opt-in in `global.json` is required — without it `dotnet test`
fails outright on the .NET 10 SDK, which no longer supports the VSTest target.

- `WinTabber.Events.Tests` — TUnit; shortcut model tests (trigger matching, conflict detection, commit tracking)
- `WinTabber.Infrastructure.Tests` — TUnit with a 3x retry policy in `GlobalSetup.cs`; contains `TrieNodeTests`, settings persistence, and infrastructure-level tests
- `WinTabber.Api.Tests` — TUnit
- `Wintabber.SessionsTest` — Console app for manual session/audio testing (not a test framework)
