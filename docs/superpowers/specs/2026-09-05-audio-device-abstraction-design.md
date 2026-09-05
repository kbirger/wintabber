# IAudioDevice Abstraction Design

Covers `.cleanup/tasks.md` T5.5, deferred out of Phase 5B (see
`docs/superpowers/specs/2026-09-04-phase-5-design.md`'s T5.3 section and
`WinTabber.Api.Media.Tests/README.md`) because it needed its own design pass. Phase 5 (5A and
5B) is complete and committed on the `cleanup` branch; this is independent follow-up work, not a
continuation of that phase's task list.

## Problem

`CoreAudioDeviceRepository`'s device-returning logic — `GetDefaultPlaybackDevice`,
`GetDefaultRecordingDevice`, `EnumerateAudioEndPoints`-backed `GetDevices()`, and
state-change-driven cache add/remove — is untestable. `IMMDeviceEnumeratorWrapper`'s three
device-returning members (`GetDefaultAudioEndpoint`, `EnumerateAudioEndPoints`, `GetDevice`)
return NAudio's `MMDevice`, whose only constructor is `internal` and takes an internal COM
interface (confirmed via reflection against the real DLL during Phase 5B). No test code can
construct one, so `FakeMMDeviceEnumeratorWrapper`'s three device-returning methods currently
just throw `NotSupportedException`, and `WinTabber.Api.Media.Tests` covers only the
`HasDefaultAudioEndpoint == false` path and callback registration.

`CoreAudioSessionRepository` is explicitly **out of scope** here — its
`AudioSessionManager` dependency is a separate COM-backed type with the same
no-public-constructor problem, and fixing it needs its own seam (there isn't one today). This
spec does not attempt it; the test README's existing note about that gap stays accurate after
this work lands.

## Design

### `IAudioDevice`

A new interface, `IAudioDevice` (`WinTabber.Api.Media/CoreAudio/IAudioDevice.cs`), becomes the
type that flows through every consumer above the enumerator boundary — replacing both `MMDevice`
and today's concrete `CoreAudioDeviceWrapper` as a cross-boundary type:

```csharp
public interface IAudioDevice
{
    string Id { get; }                    // cached at construction
    DataFlow DataFlow { get; }            // cached
    string DisplayName { get; }           // cached
    string FriendlyName { get; }          // cached
    string DeviceFriendlyName { get; }    // cached
    bool CanSetVolume { get; }            // cached
    bool CanMute { get; }                 // cached
    DeviceState State { get; }            // cached (snapshot as of GetDevice/enumerate call)

    float MasterVolumeLevelScalar { get; set; }  // live passthrough
    bool Mute { get; set; }                      // live passthrough
    IObservable<(float MasterVolume, bool Muted)> VolumeChanged { get; }  // live

    AudioSessionManager AudioSessionManager { get; }  // live passthrough, NOT fakeable (COM-backed)

    IObservable<Unit> SetVolume(float volume);
    IObservable<Unit> SetMute(bool isMuted);
}
```

The cached/live split exactly mirrors current behavior — no behavior changes, only a seam
inserted at the type boundary:

- **Cached fields** (`Id`, `DataFlow`, names, `CanSetVolume`, `CanMute`, `State`) match what
  today's `CoreAudioDeviceWrapper` already eagerly copies out of `MMDevice` at construction time,
  because `MMDevice`/COM must only be touched on the STA thread it was created on
  (`STAScheduler`). These values don't change for a given device instance's lifetime in the
  current design — a state transition to `Active` already produces a brand-new wrapper instance
  today (`CoreAudioDeviceRepository.GetDevices()`'s state-change handler calls
  `_enumerator.GetDevice(change.DeviceId)` and constructs a fresh wrapper), so caching `State` at
  construction changes nothing observable.
- **Live passthroughs** (`MasterVolumeLevelScalar`, `Mute`, `VolumeChanged`,
  `AudioSessionManager`) match what `CoreAudioDevicesMonitor.Watch` and
  `CoreAudioSessionRepository.GetDeviceSessions` already read directly off `MMDevice`/
  `AudioEndpointVolume` today, not off any cached snapshot.

### Implementations

- **`CoreAudioDevice(MMDevice device, IScheduler scheduler) : IAudioDevice`**
  (`WinTabber.Api.Media/CoreAudio/Models/CoreAudioDevice.cs`) — the renamed, repurposed
  `CoreAudioDeviceWrapper`. Constructed only inside `MMDeviceEnumeratorWrapper`, the sole
  remaining place that touches raw `MMDevice`. Same eager-copy-at-construction pattern as today's
  class for the cached fields; `VolumeChanged` wraps `AudioEndpointVolume.OnVolumeNotification`
  via `Observable.FromEvent`, replacing `CoreAudioDevicesMonitor.GetVolumeChanged`'s current
  direct subscription.
- **`FakeAudioDevice : IAudioDevice`** (new,
  `WinTabber.Api.Media.Tests/Fakes/FakeAudioDevice.cs`) — plain mutable POCO with settable
  properties and a `Subject<(float MasterVolume, bool Muted)>` backing `VolumeChanged`, freely
  constructible by test code. `AudioSessionManager` throws `NotSupportedException` (documented
  inline, same pattern as `FakeMMDeviceEnumeratorWrapper`'s existing not-fakeable members) since
  it remains COM-backed and out of scope.

### Enumerator boundary

`IMMDeviceEnumeratorWrapper`'s three device-returning methods change return type from `MMDevice`
to `IAudioDevice`:

```csharp
IAudioDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role);
IEnumerable<IAudioDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState);
IAudioDevice GetDevice(string id);
```

`MMDeviceEnumeratorWrapper` gains an `IScheduler` constructor dependency (keyed
`STAScheduler.Key`, the same pattern `CoreAudioDeviceRepository` already uses) to construct
`CoreAudioDevice` instances from the raw `MMDevice`s NAudio's `MMDeviceEnumerator` returns.
`WinTabberUI/Bootstrapper.cs`'s registration changes from a bare
`AddSingleton<IMMDeviceEnumeratorWrapper, MMDeviceEnumeratorWrapper>()` to a factory lambda
injecting the keyed scheduler.

### Ripple simplification

Because the enumerator now hands back fully-formed `IAudioDevice`s, wrapping responsibility
moves out of `CoreAudioDeviceRepository` and down into `MMDeviceEnumeratorWrapper`, where it
belongs:

- `CoreAudioDeviceRepository.GetDevices()` no longer constructs
  `new CoreAudioDeviceWrapper(device, Scheduler)` — it caches what the enumerator already
  returns. Cache key changes from `device.Device.ID` to `device.Id` (same value, one less hop).
- `CoreAudioDeviceRepository.Watch` / `CoreAudioDevicesMonitor.Watch` take `IAudioDevice` instead
  of `MMDevice`. `GetVolumeChanged` becomes `device.VolumeChanged` instead of reaching through
  `.AudioEndpointVolume.OnVolumeNotification` directly. Initial-value reads become
  `device.MasterVolumeLevelScalar` / `device.Mute`.
- `AudioDeviceService.WatchDevice` calls `_repository.Watch(device)` directly instead of
  `_repository.Watch(device.Device)` — the `CoreAudioDeviceWrapper.Device` escape-hatch property
  is deleted entirely, since nothing needs to reach through to a raw `MMDevice` anymore.
- `CoreAudioSessionRepository.GetDeviceSessions` and `CoreAudioSessionWrapper`'s constructor
  parameter change from `CoreAudioDeviceWrapper` to `IAudioDevice`; `device.Device.AudioSessionManager`
  becomes `device.AudioSessionManager` (passthrough, unchanged runtime behavior — still
  live/COM-backed, still untestable, just relocated onto the interface).
  `CoreAudioSessionWrapper`'s public `Device` property changes from `CoreAudioDeviceWrapper` to
  `IAudioDevice`. It's consumed across project boundaries —
  `WinTabberUI/ViewModels/MediaDebugRows.cs` (`session.Device.FriendlyName`) and
  `WinTabber.UI.Media/ViewModels/MediaSessionViewModel.cs` (passes it straight into
  `AudioDeviceService.WatchDevice`) — both read only cached `IAudioDevice` members or pass the
  reference through, so no call-site changes beyond the type itself.
- `AudioDeviceService`'s `_nativeDevices` field and `CoreAudioDeviceRepository.Devices`
  (the `[Lazy]`-generated cache) change type from `IObservableCache<CoreAudioDeviceWrapper, string>`
  to `IObservableCache<IAudioDevice, string>`. `DeviceDto.CreateItem` and every other
  `CoreAudioDeviceWrapper`-typed parameter across `WinTabber.Api.Media` switch to `IAudioDevice`.

After this change, `MMDevice` and `CoreAudioDeviceWrapper` no longer appear anywhere outside
`MMDeviceEnumeratorWrapper.cs` and `CoreAudioDevice.cs`.

### Cleanup folded in

- Rename `CoreAudioDeviceWrapper` → `CoreAudioDevice` (file move
  `CoreAudio/Models/CoreAudioDeviceWrapper.cs` → `CoreAudio/Models/CoreAudioDevice.cs`).
  `IAudioDevice` / `CoreAudioDevice` reads as an interface/real-implementation pair; every
  consumer now sees `IAudioDevice`, so the old "Wrapper" name (accurate when it wrapped a raw
  `MMDevice` visible to callers) no longer fits.
- Delete `AudioDeviceService.CanSetVolume(MMDevice)` / `CanMute(MMDevice)` — private, zero
  callers (verified via `find_referencing_symbols`), logic duplicated by `CoreAudioDevice`'s own
  cached `CanSetVolume`/`CanMute`.
- Delete `AudioDeviceSelectorViewModel.GetDevicesObservable(MMDeviceEnumerator)` — private
  static, zero callers, referenced only from an already-commented-out block above it.

Both deletions were confirmed zero-reference before this spec was written; both directly
reference the `MMDevice`/`MMDeviceEnumerator` types this refactor is narrowing exposure to, so
leaving them means reasoning about dead code that references a type we're deliberately moving
off the public seam.

### Testing

`FakeMMDeviceEnumeratorWrapper`'s three currently-`NotSupportedException` methods become real
fakes backed by test-configured `FakeAudioDevice` instances. New coverage for
`WinTabber.Api.Media.Tests`:

- `CoreAudioDeviceRepository.GetDefaultPlaybackDevice`/`GetDefaultRecordingDevice`'s
  `HasDefaultAudioEndpoint == true` branch.
- `EnumerateAudioEndPoints`-driven `GetDevices()` population.
- State-change-driven cache add/remove: `Active` (add/refresh), `Unplugged`/`Disabled`/
  `NotPresent` (remove).
- `CoreAudioDevicesMonitor.Watch`'s `VolumeChanges`/`MuteChanges` observables, driven through a
  `FakeAudioDevice`'s `VolumeChanged` subject.

`CoreAudioSessionRepository` stays uncovered — `WinTabber.Api.Media.Tests/README.md` gets
updated to drop the now-fixed device-returning-paths gap and keep the `CoreAudioSessionRepository`/
`AudioSessionManager` gap, with a note that it's unchanged by this work.

## Rejected alternatives

- **Narrower volume/mute-only abstraction, leaving `EnumerateAudioEndPoints`/`GetDevice`
  returning raw `MMDevice`.** Doesn't meet T5.5's actual goal — the device-returning paths stay
  untestable, which is the entire point of this work.
- **Test against a real or virtual audio device.** Infeasible in CI (no audio hardware, no
  virtual device driver available), and would make tests slow and machine-dependent even where
  it's possible.

## Scope check

This is a same-project (`WinTabber.Api.Media` + its test project) type-boundary refactor, no new
subsystem, one implementation plan. `WinTabberUI/Bootstrapper.cs` gets one registration-signature
change. No other project references `CoreAudioDeviceWrapper`, `MMDevice`, or
`IMMDeviceEnumeratorWrapper` outside `WinTabber.Api.Media`/`WinTabber.Api.Media.Tests` (confirmed
via the `MMDevice[^E]` sweep done while researching this spec) and `WinTabberUI/Bootstrapper.cs`'s
DI registrations.
