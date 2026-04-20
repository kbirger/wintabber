# Testability Future Work

Items deferred from the testability improvement initiative on the `audio` branch.

---

## 1. Add interfaces for core media services

`Bootstrapper.cs` still registers several services as concrete types. Add interfaces and update registrations to follow the existing `IMediaControlsStateService` / `IActiveWindowStateService` pattern.

| Interface | Concrete |
|-----------|----------|
| `ICoreAudioDeviceRepository` | `CoreAudioDeviceRepository` |
| `IAudioSessionService` | `AudioSessionService` |
| `IAudioDeviceService` | `AudioDeviceService` |
| `IMediaSessionService` | `MediaSessionService` |
| `IInstalledApplicationRepository` | `InstalledApplicationRepository` |

Update `Bootstrapper.cs` to `AddSingleton<IFoo, Foo>()` for each.

---

## 2. Move ViewModel subscriptions to `WhenActivated`

`AudioDeviceSelectorViewModel` and `MediaControlsViewModel` start Rx subscriptions in their constructors. Move subscription logic to `WhenActivated` or an explicit `Initialize()` method, and store returned disposables in a `CompositeDisposable` field.

Files: `WinTabber.UI.Media/ViewModels/AudioDeviceSelectorViewModel.cs`, `WinTabber.UI.Media/ViewModels/MediaControlsViewModel.cs`

---

## 3. Replace `Ioc.Default` with constructor injection

`Bootstrapper.Init()` configures `Ioc.Default` — a global static container that makes parallel test isolation impossible. Restrict to startup only and use constructor injection throughout `WinTabberUI`.

---

## 4. Fix static `WeakReference` in `HintBehavior`

`static WeakReference<FrameworkElement>? _activeRootRef` in `HintBehavior.cs` (line ~161) is shared across test runs and causes pollution. Move to instance scope or a per-window context.

File: `WinTabber.UI.Common/Behaviors/HintBehavior.cs`
