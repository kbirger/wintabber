# Testability Review

## Critical

### 1. Inject `IScheduler`; delete `STAScheduler`
`STAScheduler` is a static global that prevents substituting a `TestScheduler` in Rx tests.

- [ ] Add `IScheduler` parameter to each consumer's constructor
- [ ] Delete `WinTabber.Api.Media/CoreAudio/Repositories/STAScheduler.cs`

**Affected:** `CoreAudioDeviceRepository`, `CoreAudioSessionRepository`, `CoreAudioDevicesMonitor`, `MediaSessionService`, `MediaControlsStateService`

---

### 2. Add interfaces for all core services
`Bootstrapper.cs:76-82` registers everything as a concrete type. Only `IMediaControlsStateService` and `IActiveWindowStateService` follow the right pattern.

- [ ] `ICoreAudioDeviceRepository` → `CoreAudioDeviceRepository`
- [ ] `ICoreAudioSessionRepository` → `CoreAudioSessionRepository`
- [ ] `IAudioDeviceService` → `AudioDeviceService`
- [ ] `IAudioSessionService` → `AudioSessionService`
- [ ] `IMediaSessionService` → `MediaSessionService`
- [ ] `IInstalledApplicationRepository` → `InstalledApplicationRepository`
- [ ] Update `Bootstrapper.cs` to register as `AddSingleton<IFoo, Foo>()`

---

### 3. Abstract COM objects in `CoreAudioDeviceRepository`
Constructor directly calls `new MMDeviceEnumerator()` and `new PolicyConfigClient()` — both hit the Windows audio stack and fail without real hardware.

- [ ] Extract `IMMDeviceEnumeratorWrapper` interface; inject it
- [ ] Extract `IPolicyConfigClient` interface; inject it
- [ ] `CoreAudioDeviceRepository.cs:23-28`

---

### 4. Remove static process state
Static constructors in `ApplicationRef` and `WindowProcessRef` capture real PIDs/process lists once per AppDomain — impossible to reset between tests.

- [ ] Extract `IProcessRepository` wrapping `Process.GetProcesses()` / `GetProcessById()`
- [ ] Move `_currentProcessPid` capture to instance scope or inject
- [ ] `ApplicationRef.cs:12-18`, `WindowProcessRef.cs:8-12`, `WindowManager.cs:28,38`

---

### 5. Remove WPF Dispatcher from services and ViewModels
`WinTabberEventManager` creates a real `Dispatcher` on a background thread. `AudioDeviceSelectorViewModel` calls `.ObserveOnDispatcher()` in its constructor. `WindowSelectorViewModel` inherits `DependencyObject`. Unit tests constructing these deadlock or throw.

- [ ] Replace `.ObserveOnDispatcher()` with `.ObserveOn(IScheduler mainScheduler)` (injected)
- [ ] Remove `DependencyObject` inheritance from `WindowSelectorViewModel` and `DockWindowViewModel`
- [ ] `WinTabberEventManager.cs`, `AudioDeviceSelectorViewModel.cs`, `WindowSelectorViewModel.cs`

---

### 6. Move ViewModel subscription setup out of constructors
`AudioDeviceSelectorViewModel` and `MediaControlsViewModel` start Rx subscriptions during construction — pre-subscription state is untestable and cleanup is unclear.

- [ ] Move subscription logic to an `Initialize()` method
- [ ] Store returned `IDisposable`s in a `CompositeDisposable` field
- [ ] `AudioDeviceSelectorViewModel.cs`, `MediaControlsViewModel.cs:68-149`

---

### 7. Replace `Ioc.Default` service locator
`Ioc.Default` is a global static container set once at startup; parallel tests sharing it interfere with each other.

- [ ] Restrict `Ioc.Default` to top-level bootstrap code only
- [ ] Use constructor injection everywhere else in `WinTabberUI`

---

## Medium

### 8. Remove `sealed` from `AggregateSession` or add interface
`sealed` prevents mocking frameworks from creating proxies.

- [ ] Remove `sealed` from `AggregateSession.cs`, or
- [ ] Extract `IAggregateSession` interface

---

### 9. Extract `IShellApplicationSource` for `InstalledApplicationRepository`
`KnownFolderHelper.FromKnownFolderId()`, `PInvoke.SHCreateItemFromParsingName()`, and related static Shell API calls are untestable without a real Windows shell.

- [ ] Define `IShellApplicationSource` interface
- [ ] Implement `WindowsShellApplicationSource` wrapping the current static calls
- [ ] Inject into `InstalledApplicationRepository`

---

### 10. Extract `ISmtcSessionSource` for `SMTCSessionRepository`
`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` requires Windows 10 SMTC subsystem.

- [ ] Define `ISmtcSessionSource` interface
- [ ] Inject into `SMTCSessionRepository`

---

### 11. Fix static weak reference in `HintBehavior`
`static WeakReference<FrameworkElement>? _activeRootRef` (`HintBehavior.cs:161`) is shared across tests — causes test pollution.

- [ ] Move to instance scope or scope it to a per-window context

---

## Good patterns to preserve

- `IInteropProxy` / `InteropProxy` in `WinTabber.Interop` — extend this model to audio
- `IMediaControlsStateService` / `IActiveWindowStateService` — follow for all services
- TUnit + retry policy in `WinTabber.Infrastructure.Tests` — solid foundation
- `WinTabber.Events.Tests` exists but is empty — ready for event dispatch tests once the scheduler is injectable
