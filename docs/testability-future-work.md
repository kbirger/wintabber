# Testability Future Work

Items deferred from the testability improvement initiative on the `audio` branch. **All four
items below are now resolved** — see each section for what actually happened and where.

---

## 1. ~~Add interfaces for core media services~~ — DONE

Landed on `testability`: `ICoreAudioDeviceRepository`, `IAudioSessionService`,
`IAudioDeviceService`, `IMediaSessionService`, and `IInstalledApplicationRepository` all exist and
are registered in `Bootstrapper.cs`'s `AddCoreServices`. (`CoreAudioDeviceRepository` is also kept
registered as a concrete type deliberately — `AudioDeviceService` needs its internal
`SetDefaultAudioEndpoint`, not on the interface by design — see the comment at the registration.)

Original description follows.

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

## 2. ~~Move ViewModel subscriptions to `WhenActivated`~~ — DONE, two different ways

**`MediaControlsViewModel`**: genuinely moved into `WhenActivated` in T6.6 (`0501381`), storing
subscriptions in the per-activation `disposables` bag rather than a permanent field.

**`AudioDeviceSelectorViewModel`**: still subscribes from its constructor — but verified
(2026-09-10) that this is not the leak the original item assumed. It has no DI registration; it's
created fresh per activation by `AudioDeviceSelectorViewModelFactory.Create()` inside
`MediaControlsViewModel`'s `WhenActivated` block, and disposed in that block's deactivation
cleanup (`playback.Dispose(); recording.Dispose();`). A constructor-based subscription in an
object with that lifecycle doesn't outlive an activation cycle, so moving it to `WhenActivated`
would add ceremony without fixing anything. Left as-is.

Files: `WinTabber.UI.Media/ViewModels/AudioDeviceSelectorViewModel.cs`, `WinTabber.UI.Media/ViewModels/MediaControlsViewModel.cs`

---

## 3. ~~Replace `Ioc.Default` with constructor injection~~ — DONE (turned out to be dead code)

**Resolved 2026-09-10.** Checked for actual consumers before attempting the "throughout
`WinTabberUI`" refactor the original item describes: no XAML markup extension, no
`Ioc.Default.GetService` call, nothing anywhere in the tree resolved through it. It was configured
in `Bootstrapper.Init()` and never read from again. Deleted the `Ioc ioc = Ioc.Default;
ioc.ConfigureServices(serviceProvider);` lines and the now-unused
`CommunityToolkit.Mvvm.DependencyInjection` import — no broader refactor was needed because there
was nothing depending on it.

---

## 4. ~~Fix static `WeakReference` in `HintBehavior`~~ — DONE

Landed on `testability`: `WinTabber.UI.Common/Behaviors/HintActivationScope.cs` replaces the
static `WeakReference<FrameworkElement>? _activeRootRef` with per-scope state, removing the
cross-test-run pollution. See `WinTabber.UI.Common.Tests/README.md` for the coverage this enabled
(`HintBehaviorTests`, `HintBehaviorDesktopTests`).

File: `WinTabber.UI.Common/Behaviors/HintBehavior.cs`
