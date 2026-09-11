# Testability Review

## Status — 2026-09-11

Re-verified every item against current code (not just this doc's checkboxes) before picking up
further work here. **Only items 9 and 10 are still genuinely open.** Items 1–8 and 11 were all
resolved by earlier work (the `testability` branch and the audio-device-abstraction work) without
this doc being updated to match — see each item's resolution note below for what actually
happened and why. Item 5 has one deliberately-left-open piece (`WinTabberEventManager`'s STA
message-pump thread), kept for a documented Win32 constraint, not an oversight.

## Critical

### 1. ~~Inject `IScheduler`; delete `STAScheduler`~~ — DONE (except the delete, and that's fine)
`STAScheduler` is a static global that prevents substituting a `TestScheduler` in Rx tests.

> **Resolved 2026-09-11.** All five affected consumers already take `IScheduler` via
> constructor injection, resolved from `AddKeyedSingleton<IScheduler>(STAScheduler.Key, ...)` in
> `Bootstrapper.cs`. A unit test constructing any of them directly can pass a `TestScheduler` —
> none of them reference `STAScheduler` internally. `STAScheduler.cs` itself now does one thing:
> build the real STA `EventLoopScheduler` at the composition root (`Bootstrapper.cs`). That's a
> normal composition-root responsibility, not global mutable state — deleting it would just move
> the same thread-creation code inline into `Bootstrapper`, with no testability gain. Left as-is.

- [x] Add `IScheduler` parameter to each consumer's constructor
- [x] ~~Delete `WinTabber.Api.Media/CoreAudio/Repositories/STAScheduler.cs`~~ — kept; see above

**Affected:** `CoreAudioDeviceRepository`, `CoreAudioSessionRepository`, `CoreAudioDevicesMonitor`, `MediaSessionService`, `MediaControlsStateService`

---

### 2. ~~Add interfaces for all core services~~ — DONE
`Bootstrapper.cs:76-82` registers everything as a concrete type. Only `IMediaControlsStateService` and `IActiveWindowStateService` follow the right pattern.

> **Resolved on `testability`.** All six interfaces exist and are registered in
> `Bootstrapper.cs`'s `AddCoreServices`. See `docs/testability-future-work.md` item 1.

- [x] `ICoreAudioDeviceRepository` → `CoreAudioDeviceRepository`
- [x] `ICoreAudioSessionRepository` → `CoreAudioSessionRepository`
- [x] `IAudioDeviceService` → `AudioDeviceService`
- [x] `IAudioSessionService` → `AudioSessionService`
- [x] `IMediaSessionService` → `MediaSessionService`
- [x] `IInstalledApplicationRepository` → `InstalledApplicationRepository`
- [x] Update `Bootstrapper.cs` to register as `AddSingleton<IFoo, Foo>()`

---

### 3. ~~Abstract COM objects in `CoreAudioDeviceRepository`~~ — DONE
Constructor directly calls `new MMDeviceEnumerator()` and `new PolicyConfigClient()` — both hit the Windows audio stack and fail without real hardware.

> **Resolved**, landed as part of the `2026-09-05-audio-device-abstraction` work. Constructor is
> now `CoreAudioDeviceRepository(IScheduler scheduler, IMMDeviceEnumeratorWrapper enumerator)` —
> no raw COM construction. `IPolicyConfigClientWrapper` abstracts `PolicyConfigClient` the same
> way; it's built behind a lazy `IObservable<IPolicyConfigClientWrapper>` factory rather than
> constructor-injected (COM apartment-affinity means it must be created on the STA scheduler
> thread, not at the composition root), which is a deliberate, already-testable pattern, not a gap.

- [x] Extract `IMMDeviceEnumeratorWrapper` interface; inject it
- [x] Extract `IPolicyConfigClient` interface; inject it (as `IPolicyConfigClientWrapper`)
- [x] `CoreAudioDeviceRepository.cs:23-28`

---

### 4. ~~Remove static process state~~ — DONE
Static constructors in `ApplicationRef` and `WindowProcessRef` capture real PIDs/process lists once per AppDomain — impossible to reset between tests.

> **Resolved.** `IProcessRepository`/`ProcessRepository` exist (`WinTabber.Api.Windowing/`), with
> `FakeProcessRepository` in `WinTabber.Api.Windowing.Tests/Fakes/`. `ApplicationRef` and
> `WindowProcessRef` no longer have static state at all — process lookups go through
> `WindowManager.ProcessRepository`, an instance member.

- [x] Extract `IProcessRepository` wrapping `Process.GetProcesses()` / `GetProcessById()`
- [x] Move `_currentProcessPid` capture to instance scope or inject
- [x] `ApplicationRef.cs:12-18`, `WindowProcessRef.cs:8-12`, `WindowManager.cs:28,38`

---

### 5. ~~Remove WPF Dispatcher from services and ViewModels~~ — mostly DONE; one piece left by design
`WinTabberEventManager` creates a real `Dispatcher` on a background thread. `AudioDeviceSelectorViewModel` calls `.ObserveOnDispatcher()` in its constructor. `WindowSelectorViewModel` inherits `DependencyObject`. Unit tests constructing these deadlock or throw.

> **Resolved, three of four parts.** `AudioDeviceSelectorViewModel` no longer calls
> `.ObserveOnDispatcher()` (`e94d8cd`). `WindowSelectorViewModel` is `ReactiveObject`, not
> `DependencyObject`. `DockWindowViewModel` is `ReactiveObject` too. **Left open:**
> `WinTabberEventManager.GetScheduler()` (`WinTabberEventManager.cs:316`) still builds a
> `Dispatcher`-pumping STA thread — but per the comment at its one call site (`Init()`, line 53),
> this is deliberate: `RegisterHotKey` binds to the thread that pumps messages for its hidden
> window, so there must be exactly one scheduler instance for the object's lifetime, created on a
> real STA/message-pump thread. This is a genuine Win32 constraint, not an oversight — abstracting
> it would need a fake message-pump thread in tests, which buys little since `WinTabberEventManager`
> is the outermost glue class, not logic worth unit testing in isolation.

- [x] Replace `.ObserveOnDispatcher()` with `.ObserveOn(IScheduler mainScheduler)` (injected)
- [x] Remove `DependencyObject` inheritance from `WindowSelectorViewModel` and `DockWindowViewModel`
- [ ] `WinTabberEventManager.cs` — left open by design, see note above

---

### 6. ~~Move ViewModel subscription setup out of constructors~~ — DONE, two different ways
`AudioDeviceSelectorViewModel` and `MediaControlsViewModel` start Rx subscriptions during construction — pre-subscription state is untestable and cleanup is unclear.

> **Resolved.** `MediaControlsViewModel` moved into `WhenActivated` (T6.6, `0501381`).
> `AudioDeviceSelectorViewModel` still subscribes in its constructor, but verified this isn't a
> leak given its per-activation factory lifecycle — see `docs/testability-future-work.md` item 2
> for the full rationale on why moving it would add ceremony without fixing anything real.

- [x] Move subscription logic to an `Initialize()` method
- [x] Store returned `IDisposable`s in a `CompositeDisposable` field
- [x] `AudioDeviceSelectorViewModel.cs`, `MediaControlsViewModel.cs:68-149`

---

### 7. ~~Replace `Ioc.Default` service locator~~ — DONE (turned out to be dead code)
`Ioc.Default` is a global static container set once at startup; parallel tests sharing it interfere with each other.

> **Resolved 2026-09-10.** No consumer anywhere in the tree resolved through `Ioc.Default` — it
> was configured in `Bootstrapper.Init()` and never read from again. Deleted rather than
> refactored. See `docs/testability-future-work.md` item 3.

- [x] Restrict `Ioc.Default` to top-level bootstrap code only
- [x] Use constructor injection everywhere else in `WinTabberUI`

---

## Medium

### 8. ~~Remove `sealed` from `AggregateSession` or add interface~~ — DONE
`sealed` prevents mocking frameworks from creating proxies.

> **Resolved.** `AggregateSession` (`WinTabber.UI.Media/Models/AggregateSession.cs`) is not sealed.

- [x] Remove `sealed` from `AggregateSession.cs`, or
- [x] Extract `IAggregateSession` interface

---

### 9. ~~Extract `IShellApplicationSource` for `InstalledApplicationRepository`~~ — DONE
`KnownFolderHelper.FromKnownFolderId()`, `PInvoke.SHCreateItemFromParsingName()`, and related static Shell API calls are untestable without a real Windows shell.

> **Resolved 2026-09-11.** See `docs/superpowers/plans/2026-09-11-shell-smtc-source-abstraction.md`.
> `ShellObject` itself still has no accessible test constructor, so this makes the *acquisition*
> step substitutable, not full shell-item processing — see
> `WinTabber.Api.Media.Tests/README.md` for what that does and doesn't unlock.

- [x] Define `IShellApplicationSource` interface
- [x] Implement `WindowsShellApplicationSource` wrapping the current static calls
- [x] Inject into `InstalledApplicationRepository`

---

### 10. ~~Extract `ISmtcSessionSource` for `SMTCSessionRepository`~~ — DONE
`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` requires Windows 10 SMTC subsystem.

> **Resolved 2026-09-11.** See `docs/superpowers/plans/2026-09-11-shell-smtc-source-abstraction.md`.
> Same caveat as item 9: makes acquisition substitutable, not full session processing.

- [x] Define `ISmtcSessionSource` interface
- [x] Inject into `SMTCSessionRepository`

---

### 11. ~~Fix static weak reference in `HintBehavior`~~ — DONE
`static WeakReference<FrameworkElement>? _activeRootRef` (`HintBehavior.cs:161`) is shared across tests — causes test pollution.

> **Resolved.** `WinTabber.UI.Common/Behaviors/HintActivationScope.cs` replaces the static field
> with per-scope state. See `docs/testability-future-work.md` item 4.

- [x] Move to instance scope or scope it to a per-window context

---

## Good patterns to preserve

- `IInteropProxy` / `InteropProxy` in `WinTabber.Interop` — extend this model to audio
- `IMediaControlsStateService` / `IActiveWindowStateService` — follow for all services
- TUnit + retry policy in `WinTabber.Infrastructure.Tests` — solid foundation
- `WinTabber.Events.Tests` exists but is empty — ready for event dispatch tests once the scheduler is injectable
