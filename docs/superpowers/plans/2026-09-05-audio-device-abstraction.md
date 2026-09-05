# IAudioDevice Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `MMDevice`/`CoreAudioDeviceWrapper` with a new `IAudioDevice` interface as the
type that flows through `WinTabber.Api.Media`'s device-returning seam, so
`CoreAudioDeviceRepository`'s previously-untestable device-returning logic gets real test
coverage.

**Architecture:** `IMMDeviceEnumeratorWrapper`'s three device-returning members change from
returning `MMDevice` to returning `IAudioDevice`. `CoreAudioDeviceWrapper` (renamed
`CoreAudioDevice`) becomes the sole real implementation, constructed only inside
`MMDeviceEnumeratorWrapper` — the one remaining place that touches raw `MMDevice`. Every consumer
above that boundary (`CoreAudioDeviceRepository`, `CoreAudioDevicesMonitor`, `AudioDeviceService`,
`CoreAudioSessionRepository`, `CoreAudioSessionWrapper`) is narrowed to `IAudioDevice`. A new
`FakeAudioDevice` gives tests a freely-constructible stand-in.

**Tech Stack:** C# / .NET 10, NAudio (WASAPI/CoreAudio), System.Reactive, DynamicData, TUnit.

**Spec:** `docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md` (read before
starting — this plan implements it and assumes its reasoning, including the cached-vs-live field
split and the two approved cleanups).

## Global Constraints

- Confirm the current baseline before Task 1: `dotnet test --solution WinTabber.slnx` (expected
  92 passed, per `.cleanup/HANDOFF.md` — confirm this hasn't drifted).
- Several early tasks intentionally leave the solution **not building** — this refactor touches
  eight files that only compile together. Each task's "Build" step says exactly what's expected
  (which errors, and why they're fine at that point). Only Task 4, Task 5, and Task 7 require a
  fully green build/test.
- Prefer Serena's symbolic tools (`find_referencing_symbols`, `replace_symbol_body`,
  `search_for_pattern`) over hand-editing when locating or checking whether something is used.
  Per `.cleanup/HANDOFF.md`, `rename_symbol` is unreliable while the solution doesn't build — this
  plan uses direct file edits instead of `rename_symbol` for exactly that reason.
- Commit after each task. Commit style: `chore: T5.5 <n> — <summary>` (this work is
  `.cleanup/tasks.md`'s T5.5).
- Do not run `--no-verify` or skip hooks.

---

### Task 1: Create `IAudioDevice`; rename `CoreAudioDeviceWrapper` → `CoreAudioDevice`

**Files:**
- Create: `WinTabber.Api.Media/CoreAudio/IAudioDevice.cs`
- Delete: `WinTabber.Api.Media/CoreAudio/Models/CoreAudioDeviceWrapper.cs`
- Create: `WinTabber.Api.Media/CoreAudio/Models/CoreAudioDevice.cs`

**Interfaces:**
- Produces: `IAudioDevice` (namespace `WinTabber.Api.Media.CoreAudio`) and
  `CoreAudioDevice(MMDevice device, IScheduler scheduler) : IAudioDevice` (namespace
  `WinTabber.Api.Media.CoreAudio.Models`) — the exact member set every later task depends on.

- [ ] **Step 1: Create `IAudioDevice.cs`**

```csharp
using System.Reactive;
using NAudio.CoreAudioApi;

namespace WinTabber.Api.Media.CoreAudio;

/// <summary>
/// Abstraction over NAudio's <see cref="MMDevice"/>, whose only constructor is internal and takes
/// an internal COM interface — no test code can construct one. Cached members mirror fields that
/// are safe to read off the STA thread the underlying device was created on (copied out at
/// construction, same as before this interface existed); the rest read through live, exactly as
/// today's code reads them off <c>MMDevice</c>/<c>AudioEndpointVolume</c> directly.
/// </summary>
public interface IAudioDevice
{
    string Id { get; }
    DataFlow DataFlow { get; }
    string DisplayName { get; }
    string FriendlyName { get; }
    string DeviceFriendlyName { get; }
    bool CanSetVolume { get; }
    bool CanMute { get; }
    DeviceState State { get; }

    float MasterVolumeLevelScalar { get; set; }
    bool Mute { get; set; }
    IObservable<(float MasterVolume, bool Muted)> VolumeChanged { get; }

    /// <summary>
    /// Not fakeable in tests — COM-backed, no accessible constructor, same limitation
    /// <see cref="MMDevice"/> itself had. Consumed only by <c>CoreAudioSessionRepository</c>,
    /// which stays untested for the same reason.
    /// </summary>
    AudioSessionManager AudioSessionManager { get; }

    IObservable<Unit> SetVolume(float volume);
    IObservable<Unit> SetMute(bool isMuted);
}
```

- [ ] **Step 2: Delete the old wrapper file**

```bash
rm WinTabber.Api.Media/CoreAudio/Models/CoreAudioDeviceWrapper.cs
```

- [ ] **Step 3: Create `CoreAudioDevice.cs`**

```csharp
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class CoreAudioDevice(MMDevice device, IScheduler scheduler) : IAudioDevice
{
    private readonly MMDevice _device = device;
    private readonly IScheduler _scheduler = scheduler;

    // Create properties for being able to safely access some fields without being on the right thread
    public string Id { get; } = device.ID;
    public DataFlow DataFlow { get; } = device.DataFlow;
    public string DisplayName { get; } = device.FriendlyName ?? device.DeviceFriendlyName;
    public string FriendlyName { get; } = device.FriendlyName ?? "Unknown Device";
    public string DeviceFriendlyName { get; } = device.DeviceFriendlyName;
    public bool CanSetVolume { get; } =
        device.AudioEndpointVolume.VolumeRange.MaxDecibels > device.AudioEndpointVolume.VolumeRange.MinDecibels;
    public bool CanMute { get; } = device.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Mute);
    public DeviceState State { get; } = device.State;

    public float MasterVolumeLevelScalar
    {
        get => _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        set => _device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
    }

    public bool Mute
    {
        get => _device.AudioEndpointVolume.Mute;
        set => _device.AudioEndpointVolume.Mute = value;
    }

    public AudioSessionManager AudioSessionManager => _device.AudioSessionManager;

    public IObservable<(float MasterVolume, bool Muted)> VolumeChanged =>
        Observable
            .Defer(() =>
            {
                var audioEndpointVolume = _device.AudioEndpointVolume;
                return Observable.FromEvent<AudioEndpointVolumeNotificationDelegate, AudioVolumeNotificationData>(
                    h =>
                    {
                        if (audioEndpointVolume is not null)
                            audioEndpointVolume.OnVolumeNotification += h;
                    },
                    h =>
                    {
                        if (audioEndpointVolume is not null)
                            audioEndpointVolume.OnVolumeNotification -= h;
                    }
                );
            })
            .Select(change => (change.MasterVolume, change.Muted))
            .SubscribeOn(_scheduler);

    public IObservable<Unit> SetVolume(float volume)
    {
        return Observable.Start(() =>
        {
            if (Math.Abs(_device.AudioEndpointVolume.MasterVolumeLevelScalar - volume) > .01)
            {
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
        }, _scheduler);
    }

    public IObservable<Unit> SetMute(bool isMuted)
    {
        return Observable.Start(() =>
        {
            if (_device.AudioEndpointVolume.Mute != isMuted)
            {
                _device.AudioEndpointVolume.Mute = isMuted;
            }
        }, _scheduler);
    }
}
```

This preserves `CoreAudioDeviceWrapper`'s exact original semantics for every cached field and for
`SetVolume`/`SetMute`'s epsilon-guarded writes; `VolumeChanged` is `CoreAudioDevicesMonitor`'s old
`GetVolumeChanged(MMDevice)` logic, relocated here (Task 3 removes it from the monitor).

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: does **not** build. `CS0246: The type or namespace name 'CoreAudioDeviceWrapper' could
not be found` in `WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDeviceRepository.cs`,
`WinTabber.Api.Media/CoreAudio/Services/AudioDeviceService.cs`,
`WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioSessionRepository.cs`, and
`WinTabber.Api.Media/CoreAudio/Models/CoreAudioSessionWrapper.cs` — all four still reference the
deleted type name. This is expected; Tasks 2–4 fix them in sequence.

- [ ] **Step 5: Commit**

```bash
git add WinTabber.Api.Media/CoreAudio/IAudioDevice.cs WinTabber.Api.Media/CoreAudio/Models/CoreAudioDevice.cs
git rm WinTabber.Api.Media/CoreAudio/Models/CoreAudioDeviceWrapper.cs
git commit -m "chore: T5.5 1 — add IAudioDevice, rename CoreAudioDeviceWrapper to CoreAudioDevice"
```

---

### Task 2: Update `IMMDeviceEnumeratorWrapper`/`MMDeviceEnumeratorWrapper`, DI registration

**Files:**
- Modify: `WinTabber.Api.Media/CoreAudio/IMMDeviceEnumeratorWrapper.cs`
- Modify: `WinTabber.Api.Media/CoreAudio/MMDeviceEnumeratorWrapper.cs`
- Modify: `WinTabberUI/Bootstrapper.cs`

**Interfaces:**
- Consumes: `IAudioDevice`, `CoreAudioDevice(MMDevice, IScheduler)` (Task 1)
- Produces: `IMMDeviceEnumeratorWrapper.GetDefaultAudioEndpoint`/`EnumerateAudioEndPoints`/
  `GetDevice` now return `IAudioDevice`/`IEnumerable<IAudioDevice>` — the signatures Task 3 and
  Task 6 depend on.

- [ ] **Step 1: Update the interface**

Replace `WinTabber.Api.Media/CoreAudio/IMMDeviceEnumeratorWrapper.cs`'s full contents:

```csharp
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio;

public interface IMMDeviceEnumeratorWrapper : IDisposable
{
    IAudioDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role);
    bool HasDefaultAudioEndpoint(DataFlow dataFlow, Role role);
    IEnumerable<IAudioDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState);
    IAudioDevice GetDevice(string id);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}
```

- [ ] **Step 2: Update the real implementation**

Replace `WinTabber.Api.Media/CoreAudio/MMDeviceEnumeratorWrapper.cs`'s full contents:

```csharp
using System.Reactive.Concurrency;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio;

public sealed class MMDeviceEnumeratorWrapper(IScheduler scheduler) : IMMDeviceEnumeratorWrapper, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly IScheduler _scheduler = scheduler;

    public IAudioDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role) =>
        new CoreAudioDevice(_enumerator.GetDefaultAudioEndpoint(dataFlow, role), _scheduler);

    public bool HasDefaultAudioEndpoint(DataFlow dataFlow, Role role) =>
        _enumerator.HasDefaultAudioEndpoint(dataFlow, role);

    public IEnumerable<IAudioDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState) =>
        _enumerator
            .EnumerateAudioEndPoints(dataFlow, deviceState)
            .Select(device => (IAudioDevice)new CoreAudioDevice(device, _scheduler));

    public IAudioDevice GetDevice(string id) => new CoreAudioDevice(_enumerator.GetDevice(id), _scheduler);

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.RegisterEndpointNotificationCallback(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.UnregisterEndpointNotificationCallback(client);

    public void Dispose() => _enumerator.Dispose();
}
```

Note the added `using System.Linq;`-requiring `.Select(...)` call — `System.Linq` is covered by
this project's implicit usings (confirmed: the original file already used `IEnumerable<MMDevice>`
with no explicit `System.Collections.Generic` using).

- [ ] **Step 3: Update the DI registration in `Bootstrapper.cs`**

In `WinTabberUI/Bootstrapper.cs`'s `AddDomainModels` method, change:

```csharp
            .AddSingleton<IMMDeviceEnumeratorWrapper, MMDeviceEnumeratorWrapper>()
```

to:

```csharp
            .AddSingleton<IMMDeviceEnumeratorWrapper>(sp =>
                new MMDeviceEnumeratorWrapper(sp.GetRequiredKeyedService<IScheduler>(STAScheduler.Key)))
```

This matches the existing factory-lambda pattern already used two lines below it for
`CoreAudioDeviceRepository`.

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: still fails, same four `CoreAudioDeviceWrapper`-not-found errors as Task 1 (those
consumers haven't changed yet) — confirm no *new* error categories appeared (i.e. nothing else
broke from this step).

- [ ] **Step 5: Commit**

```bash
git add WinTabber.Api.Media/CoreAudio/IMMDeviceEnumeratorWrapper.cs WinTabber.Api.Media/CoreAudio/MMDeviceEnumeratorWrapper.cs WinTabberUI/Bootstrapper.cs
git commit -m "chore: T5.5 2 — IMMDeviceEnumeratorWrapper returns IAudioDevice; DI constructs it with a scheduler"
```

---

### Task 3: Update `CoreAudioDeviceRepository` and `CoreAudioDevicesMonitor`

**Files:**
- Modify: `WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDeviceRepository.cs`
- Modify: `WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDevicesMonitor.cs`

**Interfaces:**
- Consumes: `IAudioDevice` (Task 1), `IMMDeviceEnumeratorWrapper` returning `IAudioDevice`
  (Task 2)
- Produces: `CoreAudioDeviceRepository.Watch(IAudioDevice)`,
  `CoreAudioDeviceRepository.Devices : IObservableCache<IAudioDevice, string>`,
  `CoreAudioDeviceRepository.GetDefaultPlaybackDevice()`/`GetDefaultRecordingDevice()` returning
  `IAudioDevice?` — what Task 4 and Task 7's tests call.

- [ ] **Step 1: Update `CoreAudioDeviceRepository.cs`**

Change `GetDefaultPlaybackDevice`/`GetDefaultRecordingDevice`'s return type and `Watch`'s
parameter type:

```csharp
    public IAudioDevice? GetDefaultPlaybackDevice()
    {
        if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        return null;
    }

    public IAudioDevice? GetDefaultRecordingDevice()
    {
        if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }
        return null;
    }

    public DeviceEvents Watch(IAudioDevice device)
    {
        return _monitor.Watch(device);
    }
```

Change `GetDevices()` — the `[Lazy]`-backed `Devices` cache — to stop constructing
`CoreAudioDeviceWrapper` (the enumerator already returns fully-formed `IAudioDevice`s), and change
its cache key from `device.Device.ID` to `device.Id`:

```csharp
    [Lazy]
    private IObservableCache<IAudioDevice, string> GetDevices()
    {
        return ObservableChangeSet
            .Create<IAudioDevice, string>(
                cache =>
                {
                    var dispose = new CompositeDisposable(
                        Disposable.Create(() =>
                        {
                            Debug.WriteLine("disposing");
                        })
                    );
                    return Scheduler.Schedule(() =>
                    {
                        DevicesObservable
                            .Take(1)
                            .Subscribe(devices =>
                            {
                                Debug.WriteLine(
                                    $"Devices fetched on thread {Environment.CurrentManagedThreadId} - {Thread.CurrentThread.Name} - {Thread.CurrentThread.GetApartmentState()}"
                                );
                                cache.AddOrUpdate(devices);

                                var removalSubscription = _monitor.DeviceRemovals.Subscribe(deviceId =>
                                {
                                    cache.Remove(deviceId);
                                });

                                var additionSubscription = _monitor.DeviceAdditions.Subscribe(deviceId =>
                                {
                                    var device = _enumerator.GetDevice(deviceId);
                                    if (device.State == DeviceState.Active)
                                    {
                                        cache.AddOrUpdate(device);
                                        cache.Refresh(device);
                                    }
                                });
                                var defaultSubscription = _monitor.DefaultDeviceChanges.Subscribe(change =>
                                {
                                    Debug.WriteLine(
                                        $"Device {change.DeviceId} is now default {change.DataFlow} device"
                                    );
                                    cache.Refresh();
                                });

                                var stateChangeSubscription = _monitor.DeviceStateChanges.Subscribe(change =>
                                {
                                    Debug.WriteLine(
                                        $"Device state changed on thread {Environment.CurrentManagedThreadId} - {change.NewState}"
                                    );
                                    if (
                                        change.NewState.In(
                                            DeviceState.Unplugged,
                                            DeviceState.Disabled,
                                            DeviceState.NotPresent
                                        )
                                    )
                                    {
                                        Debug.WriteLine(
                                            $"Device state changed on thread {Environment.CurrentManagedThreadId} - {change.NewState}"
                                        );
                                        cache.Remove(change.DeviceId);
                                    }
                                    else if (change.NewState == DeviceState.Active)
                                    {
                                        var device = _enumerator.GetDevice(change.DeviceId);
                                        cache.AddOrUpdate(device);
                                        cache.Refresh(device);
                                    }
                                });

                                var compositeDisposable = new CompositeDisposable
                                {
                                    removalSubscription,
                                    additionSubscription,
                                    stateChangeSubscription,
                                }.DisposeWith(dispose);
                            })
                            .DisposeWith(dispose);
                    });
                },
                device => device.Id
            )
            .DisposeMany()
            .SubscribeOn(Scheduler)
            .ObserveOn(Scheduler)
            .AsObservableCache();
    }
```

Change `GetDevicesObservable()`'s element type from `MMDevice` to `IAudioDevice`:

```csharp
    [Lazy(IsPrivate = true)]
    private IObservable<IReadOnlyList<IAudioDevice>> GetDevicesObservable()
    {
        return Observable
            .Start<IReadOnlyList<IAudioDevice>>(
                () =>
                {
                    var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active).ToArray();

                    return devices;
                },
                Scheduler
            )
            // Replay last value for late subscribers
            .Replay(1)
            .RefCount()
            .ObserveOn(Scheduler);
    }
```

Leave every other member of this file (`Dispose`, `GetDefaultDevices`, `CreateDefaultDeviceChange`,
`SetDefaultAudioEndpoint`, `GetPolicyConfigClient`, the two trailing commented-out blocks)
unchanged.

- [ ] **Step 2: Update `CoreAudioDevicesMonitor.cs`**

Change `Watch` and the five private `Get*`/helper methods to take `IAudioDevice` instead of
`MMDevice`, read live volume/mute off the interface instead of `AudioEndpointVolume`, and delete
`GetVolumeChanged` (its logic moved into `CoreAudioDevice.VolumeChanged` in Task 1):

```csharp
    public DeviceEvents Watch(IAudioDevice device)
    {
        var initialValues = Observable.Start(() => (device.MasterVolumeLevelScalar, device.Mute), _scheduler);
        var volumeChanged = initialValues
            .Concat(device.VolumeChanged)
            .Replay(1)
            .RefCount();
        return new DeviceEvents
        {
            VolumeChanges = volumeChanged.Select(change => change.Item1),
            MuteChanges = volumeChanged.Select(change => change.Item2),
            PropertyChanges = GetPropertyChanges(device),
            Removed = GetRemoved(device),
            StateChanges = GetStateChanges(device),
            IsDefaultChanges = GetIsDefaultChanges(device),
        };
    }

    private IObservable<bool> GetIsDefaultChanges(IAudioDevice device)
    {
        return DefaultDeviceChanges.ObserveOn(_scheduler).Select(change => change.DeviceId == device.Id);
    }

    private IObservable<DeviceState> GetStateChanges(IAudioDevice device)
    {
        return DeviceStateChanges
            .ObserveOn(_scheduler)
            .Where(change => change.DeviceId == device.Id)
            .Select(change => change.NewState);
    }

    private IObservable<Unit> GetRemoved(IAudioDevice device)
    {
        return DeviceRemovals
            .ObserveOn(_scheduler)
            .Where(removedId => removedId == device.Id)
            .Select(_ => Unit.Default);
    }

    private IObservable<PropertyKey> GetPropertyChanges(IAudioDevice device)
    {
        return DevicePropertyChanges
            .ObserveOn(_scheduler)
            .Where(change => change.DeviceId == device.Id)
            .Select(change => change.Key);
    }
```

Delete the `GetVolumeChanged(MMDevice device)` private method entirely (the block using
`Observable.Defer`/`FromEvent<AudioEndpointVolumeNotificationDelegate, ...>` just above `Dispose`)
— nothing calls it anymore. Leave `OnDeviceStateChanged`/`OnDeviceAdded`/`OnDeviceRemoved`/
`OnDefaultDeviceChanged`/`OnPropertyValueChanged`/`AsNotificationClient`/`Dispose`/the
`DeviceStateChanges`/etc. properties/the constructor unchanged.

- [ ] **Step 3: Build**

Run: `dotnet build WinTabber.slnx`
Expected: still fails. Remaining errors should now be limited to
`WinTabber.Api.Media/CoreAudio/Services/AudioDeviceService.cs`,
`WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioSessionRepository.cs`, and
`WinTabber.Api.Media/CoreAudio/Models/CoreAudioSessionWrapper.cs` — confirm
`CoreAudioDeviceRepository.cs` and `CoreAudioDevicesMonitor.cs` themselves compile clean (no
errors reported against those two files specifically).

- [ ] **Step 4: Commit**

```bash
git add WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDeviceRepository.cs WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDevicesMonitor.cs
git commit -m "chore: T5.5 3 — CoreAudioDeviceRepository and CoreAudioDevicesMonitor operate on IAudioDevice"
```

---

### Task 4: Update `AudioDeviceService`, `CoreAudioSessionRepository`, `CoreAudioSessionWrapper`

**Files:**
- Modify: `WinTabber.Api.Media/CoreAudio/Services/AudioDeviceService.cs`
- Modify: `WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioSessionRepository.cs`
- Modify: `WinTabber.Api.Media/CoreAudio/Models/CoreAudioSessionWrapper.cs`

**Interfaces:**
- Consumes: `IAudioDevice`, `CoreAudioDeviceRepository.Watch(IAudioDevice)`,
  `CoreAudioDeviceRepository.Devices : IObservableCache<IAudioDevice, string>` (Task 3)

- [ ] **Step 1: Update `AudioDeviceService.cs`**

Change the field type, `WatchDevice`'s parameter and body, and `CreateItem`'s parameter:

```csharp
    private IObservableCache<IAudioDevice, string> _nativeDevices = repository.Devices;
```

```csharp
    public ObservableDeviceDto WatchDevice(IAudioDevice? device)
    {
        if (device == null)
        {
            return new ObservableDeviceDto
            {
                CanMute = false,
                CanSetVolume = false,
                DisplayName = string.Empty,
                Id = string.Empty,
                IsDefaultChanges = Observable.Empty<bool>(),
                PropertyChanges = Observable.Empty<PropertyKey>(),
                Removed = Observable.Empty<Unit>(),
                StateChanges = Observable.Empty<DeviceState>(),
                VolumeChanges = Observable.Empty<float>(),
                IsMutedChanges = Observable.Empty<bool>(),
                CanMuteChanges = Observable.Return(false),
                CanSetVolumeChanges = Observable.Return(false),
                SetVolume = (volume) => Observable.Empty<Unit>(),
                SetMute = (volume) => Observable.Empty<Unit>()
            };
        }

        var deviceEvents = _repository.Watch(device);
        var canSetVolume = device.CanSetVolume;
        var canMute = canSetVolume || device.CanMute;

        return new ObservableDeviceDto
        {
            CanMute = canMute,
            CanSetVolume = canSetVolume,
            DisplayName = device.DisplayName,
            Id = device.Id,

            IsDefaultChanges = deviceEvents.IsDefaultChanges,
            PropertyChanges = deviceEvents.PropertyChanges,
            Removed = deviceEvents.Removed,
            StateChanges = deviceEvents.StateChanges,
            VolumeChanges = deviceEvents
                .VolumeChanges
                .Throttle(TimeSpan.FromMilliseconds(100)),
            IsMutedChanges = deviceEvents.MuteChanges,
            CanMuteChanges = Observable.Return(device.CanMute),
            CanSetVolumeChanges = Observable.Return(device.CanSetVolume),
            SetVolume = device.SetVolume,
            SetMute = device.SetMute
        };
    }
```

Delete the two dead private methods entirely (zero callers, confirmed via
`find_referencing_symbols` while writing the spec — `CoreAudioDevice`'s own cached
`CanSetVolume`/`CanMute` already do this job):

```csharp
    private bool CanSetVolume(MMDevice device)
    {
        var range = device.AudioEndpointVolume.VolumeRange;
        return range.MaxDecibels > range.MinDecibels;
    }

    private bool CanMute(MMDevice device)
    {
        return device.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Mute);
    }
```

Change `CreateItem`'s parameter type:

```csharp
    private static DeviceDto CreateItem(IAudioDevice data)
    {
        return new DeviceDto
        {
            DeviceId = data.Id,
            DeviceFriendlyName = data.DeviceFriendlyName,
            DeviceName = data.FriendlyName,
            DataFlow = data.DataFlow,
        };
    }
```

Leave `WatchDevice(string deviceId)`, `GetDevices()`, `GetDefaultDevice`, `SetVolume`, `SetMute`,
`SetDefaultAudioEndpoint` unchanged — they already operate through `_nativeDevices`/`_repository`
without naming `CoreAudioDeviceWrapper`/`MMDevice` directly.

- [ ] **Step 2: Update `CoreAudioSessionRepository.cs`**

Add `using WinTabber.Api.Media.CoreAudio;` to the top of the file (for `IAudioDevice` — the file
currently imports `WinTabber.Api.Media.CoreAudio.Models` but not its parent namespace). Change
`Connect` and `GetDeviceSessions`'s parameter type, and the one line that reached through
`.Device`:

```csharp
    public IObservable<IChangeSet<CoreAudioSessionWrapper, string>> Connect(IAudioDevice device)
    {
        return Observable
            .Defer(() => GetDeviceSessions(device))
            .SubscribeOn(Scheduler) // ?
            .ObserveOn(Scheduler);
    }

    private IObservable<IChangeSet<CoreAudioSessionWrapper, string>> GetDeviceSessions(IAudioDevice device)
    {
        var manager = device.AudioSessionManager;
```

Leave the rest of `GetDeviceSessions`'s body (the `ObservableChangeSet.Create` block,
`GetNativeSessions`, `ObserveSessionCreation`, `WatchForEnd`, `Dispose`) unchanged — they operate
on `AudioSessionManager`/`AudioSessionControl`, not on the device abstraction.

- [ ] **Step 3: Update `CoreAudioSessionWrapper.cs`**

Add `using WinTabber.Api.Media.CoreAudio;` to the top of the file. Change the `Device` property
and the constructor's parameter type:

```csharp
    public IAudioDevice Device { get; }
```

```csharp
    public CoreAudioSessionWrapper(AudioSessionControl nativeSession, IAudioDevice device, IScheduler scheduler)
```

Leave the constructor body and every other member unchanged.

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors. This is the first fully-green build since Task 1 — every
`MMDevice`/`CoreAudioDeviceWrapper` reference outside `MMDeviceEnumeratorWrapper.cs` and
`CoreAudioDevice.cs` is gone. If it still fails, re-check
`WinTabberUI/ViewModels/MediaDebugRows.cs`'s `session.Device.FriendlyName` and
`WinTabber.UI.Media/ViewModels/MediaSessionViewModel.cs`'s
`audioDeviceService.WatchDevice(session?.NativeSession?.Device)` — both should already compile
unchanged against the new `IAudioDevice Device` property (they only read a cached member or pass
the reference through), but confirm.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: same count as the confirmed baseline (92), all passing — this task is a pure type-boundary
refactor with no behavior change, so no existing test's outcome should differ.

- [ ] **Step 6: Commit**

```bash
git add WinTabber.Api.Media/CoreAudio/Services/AudioDeviceService.cs WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioSessionRepository.cs WinTabber.Api.Media/CoreAudio/Models/CoreAudioSessionWrapper.cs
git commit -m "chore: T5.5 4 — AudioDeviceService, CoreAudioSessionRepository, CoreAudioSessionWrapper operate on IAudioDevice; delete dead CanSetVolume/CanMute(MMDevice)"
```

---

### Task 5: Delete dead `AudioDeviceSelectorViewModel.GetDevicesObservable(MMDeviceEnumerator)`

**Files:**
- Modify: `WinTabber.UI.Media/ViewModels/AudioDeviceSelectorViewModel.cs`

**Interfaces:** None (dead-code deletion only; nothing else in the solution depends on this file's
internals beyond `AudioDeviceSelectorViewModel`'s public constructor/properties, unaffected here).

- [ ] **Step 1: Delete the dead method and its associated dead comment block**

`find_referencing_symbols` confirms zero callers for `GetDevicesObservable` (private static); its
only reference is inside the already-commented-out `Create()` block directly above it in the same
file, which describes how the deleted method would have been used. Delete both together — this is
one dead cluster, not a broader cleanup pass over the file's other pre-existing comments (leave
those as-is; not in scope here).

Replace the file's full contents:

```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using DynamicData;
using NAudio.CoreAudioApi;
using ReactiveUI;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;

namespace WinTabber.UI.Media.ViewModels
{
    public partial class AudioDeviceSelectorViewModel : ReactiveObject
    {
        public AudioDeviceSelectorViewModel(AudioDeviceService deviceService, DataFlow flow)
        {
            _deviceService = deviceService;
            var devices = deviceService.Devices.Connect().Filter(device => device.DataFlow == flow);
            devices.ObserveOn(RxApp.MainThreadScheduler).Bind(out _devices).Subscribe();

            deviceService
                .GetDefaultDevice(flow)
                .Subscribe(defaultDevice =>
                {
                    SelectedDevice = defaultDevice;
                });
            //_dataFlow = dataFlow;
            //_activateFunction = activateFunction;
            //var deviceItems = devicesObservable
            //.Select(devices => devices.Select(device => new DeviceDto(device)).ToArray());
            //_devices = deviceItems
            //.ToProperty(this, vm => vm.Devices, initialValue: []);

            //deviceItems.Take(1).Subscribe(devices =>
            //{
            //    SelectedDevice = devices.SingleOrDefault(device => device.IsSelected);
            //});
        }

        //private readonly ObservableAsPropertyHelper<DeviceDto[]> _devices;
        private readonly ReadOnlyObservableCollection<DeviceDto> _devices;

        //private readonly DataFlow _dataFlow;
        //private readonly Action<MMDevice> _activateFunction;

        //public DeviceDto[] Devices => _devices.Value;
        public ReadOnlyObservableCollection<DeviceDto> Devices => _devices;

        public DeviceDto? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_selectedDevice != value)
                {
                    _selectedDevice = value;
                    if (_selectedDevice is not null)
                    {
                        // todo: catch errors
                        _deviceService
                            .SetDefaultAudioEndpoint(_selectedDevice.DeviceId)
                            .Subscribe(
                                (_) => { },
                                onError: (ex) =>
                                {
                                    Debug.WriteLine($"error setting default device {ex.Message}");
                                }
                            );
                    }
                    this.RaisePropertyChanged();
                }
            }
        }
        private DeviceDto? _selectedDevice;
        private readonly AudioDeviceService _deviceService;
    }
}
```

Only the `Create()` comment block (originally the top of the file, above the constructor) and the
`GetDevicesObservable(MMDeviceEnumerator)` method are removed; every other pre-existing comment
(`//_dataFlow = dataFlow;` and friends) is left exactly as it was — out of scope for this task.

- [ ] **Step 2: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: same as baseline (92), all passing — no test exercises this dead method.

- [ ] **Step 4: Commit**

```bash
git add WinTabber.UI.Media/ViewModels/AudioDeviceSelectorViewModel.cs
git commit -m "chore: T5.5 5 — delete dead AudioDeviceSelectorViewModel.GetDevicesObservable(MMDeviceEnumerator)"
```

---

### Task 6: Add `FakeAudioDevice`; make `FakeMMDeviceEnumeratorWrapper`'s device-returning members real

**Files:**
- Create: `WinTabber.Api.Media.Tests/Fakes/FakeAudioDevice.cs`
- Modify: `WinTabber.Api.Media.Tests/Fakes/FakeMMDeviceEnumeratorWrapper.cs`

**Interfaces:**
- Consumes: `IAudioDevice`, `IMMDeviceEnumeratorWrapper` (Tasks 1–2)
- Produces: `FakeAudioDevice` (settable properties, `VolumeChangedSubject`),
  `FakeMMDeviceEnumeratorWrapper.AddDevice(FakeAudioDevice)` and
  `.SetDefaultDevice(DataFlow, Role, FakeAudioDevice)` — what Task 7's tests call.

- [ ] **Step 1: Write `FakeAudioDevice.cs`**

```csharp
using NAudio.CoreAudioApi;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Api.Media.CoreAudio;

namespace WinTabber.Api.Media.Tests.Fakes;

/// <summary>
/// Freely constructible fake for <see cref="IAudioDevice"/> — unlike NAudio's <see cref="MMDevice"/>,
/// nothing here is COM-backed except <see cref="AudioSessionManager"/>, which stays unfakeable for
/// the same reason <see cref="FakeMMDeviceEnumeratorWrapper"/>'s device-returning members used to.
/// </summary>
public sealed class FakeAudioDevice : IAudioDevice
{
    public required string Id { get; init; }
    public DataFlow DataFlow { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string DeviceFriendlyName { get; init; } = string.Empty;
    public bool CanSetVolume { get; init; }
    public bool CanMute { get; init; }
    public DeviceState State { get; init; } = DeviceState.Active;

    public float MasterVolumeLevelScalar { get; set; }
    public bool Mute { get; set; }

    public Subject<(float MasterVolume, bool Muted)> VolumeChangedSubject { get; } = new();
    public IObservable<(float MasterVolume, bool Muted)> VolumeChanged => VolumeChangedSubject;

    public AudioSessionManager AudioSessionManager =>
        throw new NotSupportedException(
            "AudioSessionManager cannot be constructed by test code (COM-backed, no accessible constructor)."
        );

    public IObservable<Unit> SetVolume(float volume)
    {
        MasterVolumeLevelScalar = volume;
        return Observable.Return(Unit.Default);
    }

    public IObservable<Unit> SetMute(bool isMuted)
    {
        Mute = isMuted;
        return Observable.Return(Unit.Default);
    }
}
```

- [ ] **Step 2: Rewrite `FakeMMDeviceEnumeratorWrapper.cs`**

Replace the file's full contents:

```csharp
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinTabber.Api.Media.CoreAudio;

namespace WinTabber.Api.Media.Tests.Fakes;

/// <summary>
/// Hand-rolled fake for <see cref="IMMDeviceEnumeratorWrapper"/>. Every member is now a real fake
/// backed by test-configured <see cref="FakeAudioDevice"/> instances — <see cref="IAudioDevice"/>
/// replaced NAudio's <see cref="MMDevice"/> (internal constructor, no accessible way to build one)
/// as this interface's return type, so the device-returning members no longer need to throw.
/// </summary>
public sealed class FakeMMDeviceEnumeratorWrapper : IMMDeviceEnumeratorWrapper
{
    private readonly HashSet<(DataFlow Flow, Role Role)> _defaultEndpoints = [];
    private readonly Dictionary<(DataFlow Flow, Role Role), FakeAudioDevice> _defaultDevices = [];
    private readonly Dictionary<string, FakeAudioDevice> _devicesById = [];
    public List<IMMNotificationClient> RegisteredCallbacks { get; } = [];
    public List<IMMNotificationClient> UnregisteredCallbacks { get; } = [];

    public void SetHasDefaultAudioEndpoint(DataFlow flow, Role role, bool value)
    {
        if (value)
            _defaultEndpoints.Add((flow, role));
        else
            _defaultEndpoints.Remove((flow, role));
    }

    public bool HasDefaultAudioEndpoint(DataFlow dataFlow, Role role) => _defaultEndpoints.Contains((dataFlow, role));

    /// <summary>Registers <paramref name="device"/> as the default endpoint for (flow, role) and
    /// makes it resolvable via <see cref="GetDevice"/>/<see cref="EnumerateAudioEndPoints"/>.</summary>
    public void SetDefaultDevice(DataFlow flow, Role role, FakeAudioDevice device)
    {
        SetHasDefaultAudioEndpoint(flow, role, true);
        _defaultDevices[(flow, role)] = device;
        AddDevice(device);
    }

    /// <summary>Makes <paramref name="device"/> resolvable via <see cref="GetDevice"/> and, if its
    /// state matches, via <see cref="EnumerateAudioEndPoints"/>.</summary>
    public void AddDevice(FakeAudioDevice device) => _devicesById[device.Id] = device;

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) => RegisteredCallbacks.Add(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) =>
        UnregisteredCallbacks.Add(client);

    public void Dispose() { }

    public IAudioDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role) => _defaultDevices[(dataFlow, role)];

    public IEnumerable<IAudioDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState) =>
        _devicesById
            .Values
            .Where(device => (dataFlow == DataFlow.All || device.DataFlow == dataFlow) && device.State == deviceState);

    public IAudioDevice GetDevice(string id) => _devicesById[id];
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: same as baseline (92), all passing — the existing four `CoreAudioDeviceRepositoryTests`
only call `HasDefaultAudioEndpoint`/registration paths, unaffected by this rewrite.

- [ ] **Step 5: Commit**

```bash
git add WinTabber.Api.Media.Tests/Fakes/FakeAudioDevice.cs WinTabber.Api.Media.Tests/Fakes/FakeMMDeviceEnumeratorWrapper.cs
git commit -m "chore: T5.5 6 — add FakeAudioDevice; FakeMMDeviceEnumeratorWrapper's device-returning members are now real fakes"
```

---

### Task 7: Test the newly-testable paths; update the scope README; close out T5.5

**Files:**
- Modify: `WinTabber.Api.Media.Tests/CoreAudio/CoreAudioDeviceRepositoryTests.cs`
- Modify: `WinTabber.Api.Media.Tests/README.md`
- Modify: `.cleanup/tasks.md`

**Interfaces:**
- Consumes: `FakeAudioDevice`, `FakeMMDeviceEnumeratorWrapper.AddDevice`/`.SetDefaultDevice`
  (Task 6), `CoreAudioDeviceRepository.GetDefaultPlaybackDevice`/`GetDefaultRecordingDevice`/
  `Devices`/`Watch` (Task 3)

- [ ] **Step 1: Add the new tests**

Add `using System.Linq;` to the top of
`WinTabber.Api.Media.Tests/CoreAudio/CoreAudioDeviceRepositoryTests.cs` (needed for `.ToList()`/
`.Any()` below), then append these six `[Test]` methods inside the existing
`CoreAudioDeviceRepositoryTests` class, after `Dispose_UnregistersEndpointNotificationCallback`:

```csharp
    [Test]
    public async Task GetDefaultPlaybackDevice_ReturnsDevice_WhenDefaultRenderEndpointExists()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render };
        enumerator.SetDefaultDevice(DataFlow.Render, Role.Multimedia, device);
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultPlaybackDevice();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo("device-1");
    }

    [Test]
    public async Task GetDefaultRecordingDevice_ReturnsDevice_WhenDefaultCaptureEndpointExists()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice { Id = "device-2", DataFlow = DataFlow.Capture };
        enumerator.SetDefaultDevice(DataFlow.Capture, Role.Multimedia, device);
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultRecordingDevice();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo("device-2");
    }

    [Test]
    public async Task Devices_PopulatesFromEnumerateAudioEndPoints()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        enumerator.AddDevice(new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render, State = DeviceState.Active });
        enumerator.AddDevice(new FakeAudioDevice { Id = "device-2", DataFlow = DataFlow.Capture, State = DeviceState.Active });
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var devices = repository.Devices.Items.ToList();

        await Assert.That(devices.Count).IsEqualTo(2);
        await Assert.That(devices.Any(d => d.Id == "device-1")).IsTrue();
        await Assert.That(devices.Any(d => d.Id == "device-2")).IsTrue();
    }

    [Test]
    public async Task Devices_AddsDevice_WhenDeviceAddedWhileActive()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);
        var initial = repository.Devices.Items.ToList();
        await Assert.That(initial.Count).IsEqualTo(0);

        var device = new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render, State = DeviceState.Active };
        enumerator.AddDevice(device);
        var callback = enumerator.RegisteredCallbacks.Single();
        callback.OnDeviceAdded(device.Id);

        var devices = repository.Devices.Items.ToList();
        await Assert.That(devices.Count).IsEqualTo(1);
        await Assert.That(devices[0].Id).IsEqualTo("device-1");
    }

    [Test]
    public async Task Devices_RemovesDevice_WhenStateChangesToUnplugged()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render, State = DeviceState.Active };
        enumerator.AddDevice(device);
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);
        var initial = repository.Devices.Items.ToList();
        await Assert.That(initial.Count).IsEqualTo(1);

        var callback = enumerator.RegisteredCallbacks.Single();
        callback.OnDeviceStateChanged(device.Id, DeviceState.Unplugged);

        var devices = repository.Devices.Items.ToList();
        await Assert.That(devices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Watch_EmitsVolumeAndMuteChanges_FromDeviceVolumeChangedObservable()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice
        {
            Id = "device-1",
            DataFlow = DataFlow.Render,
            MasterVolumeLevelScalar = 0.5f,
            Mute = false,
        };
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var events = repository.Watch(device);
        var volumeChanges = new List<float>();
        var muteChanges = new List<bool>();
        using var volumeSub = events.VolumeChanges.Subscribe(volumeChanges.Add);
        using var muteSub = events.MuteChanges.Subscribe(muteChanges.Add);

        device.VolumeChangedSubject.OnNext((0.75f, true));

        await Assert.That(volumeChanges.Count).IsEqualTo(2);
        await Assert.That(volumeChanges[0]).IsEqualTo(0.5f);
        await Assert.That(volumeChanges[1]).IsEqualTo(0.75f);
        await Assert.That(muteChanges.Count).IsEqualTo(2);
        await Assert.That(muteChanges[0]).IsFalse();
        await Assert.That(muteChanges[1]).IsTrue();
    }
```

`Devices_AddsDevice_WhenDeviceAddedWhileActive` and `Devices_RemovesDevice_WhenStateChangesToUnplugged`
drive `CoreAudioDevicesMonitor`'s `IMMNotificationClient` callbacks directly — `enumerator.RegisteredCallbacks`
holds the monitor itself (registered in its own constructor), so calling `OnDeviceAdded`/
`OnDeviceStateChanged` on it through that interface reference simulates a real Windows device
event without needing a real device.

- [ ] **Step 2: Run the new tests**

Run: `dotnet test WinTabber.Api.Media.Tests/WinTabber.Api.Media.Tests.csproj`
Expected: 10 passed (4 existing + 6 new), 0 failed.

- [ ] **Step 3: Update the scope README**

Replace `WinTabber.Api.Media.Tests/README.md`'s full contents:

```markdown
# WinTabber.Api.Media.Tests

Deliberately narrow. Covered:

- `CoreAudioDeviceRepository`'s `HasDefaultAudioEndpoint` true/false paths, `EnumerateAudioEndPoints`-driven
  device population, state-change-driven add/remove (`OnDeviceAdded`/`OnDeviceStateChanged`), and
  its `CoreAudioDevicesMonitor` callback registration/unregistration and `Watch`'s volume/mute
  observables — all via `Fakes/FakeMMDeviceEnumeratorWrapper.cs` and `Fakes/FakeAudioDevice.cs`.
  `IAudioDevice` (see `docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md`)
  replaced `MMDevice` as the enumerator's return type specifically to make this possible.

**Not covered, and why:**

- `CoreAudioSessionRepository` entirely — its `AudioSessionManager` dependency (exposed via
  `IAudioDevice.AudioSessionManager`) is a separate COM-backed NAudio type with the same
  no-accessible-constructor problem `MMDevice` had. Out of scope for the `IAudioDevice` work;
  needs its own seam.
- `PolicyConfigClient` — raw `IPolicyConfig` COM interop, no seam, needs a real audio endpoint.
- `STAScheduler.Create()` — creates a real STA thread; not unit-testable, and not worth wrapping
  behind an interface just to verify "creates a thread." A consumer that needs to be testable
  should accept an `IScheduler` via constructor (as `CoreAudioDeviceRepository` already does) so
  a test can substitute `ImmediateScheduler`/`TestScheduler`.
- SMTC (`SMTCSessionRepository`/`SMTCSessionMonitor`/`SMTCSessionService`) and ShellApplications
  (`InstalledApplicationRepository`)'s WinRT/COM glue — no existing seam. Not manufactured
  speculatively (YAGNI) — a future task that wants coverage here needs its own design pass.

See `docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md` for the full reasoning
behind the `IAudioDevice` abstraction, and `docs/superpowers/specs/2026-09-04-phase-5-design.md`'s
T5.3 section for why this project started out this narrow.
```

- [ ] **Step 4: Mark T5.5 done in `.cleanup/tasks.md`**

Change:

```markdown
- [ ] **T5.5** *(new, plan separately)* Design an abstraction over NAudio's `MMDevice` (e.g.
```

to:

```markdown
- [x] **T5.5** *(new, plan separately)* Design an abstraction over NAudio's `MMDevice` (e.g.
```

and add a resolution line directly after this item's existing text (after "...Discovered as a
T5.3 blocker, 2026-09-04."):

```markdown
      > **Resolved:** `IAudioDevice` added; see
      > `docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md` and
      > `docs/superpowers/plans/2026-09-05-audio-device-abstraction.md`.
```

Do not touch T5.3, T5.4, or T5.6's entries — out of scope for this plan.

- [ ] **Step 5: Run the full solution suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: baseline (92) + 6 = 98 passed, 0 failed, 0 skipped.

- [ ] **Step 6: Commit**

```bash
git add WinTabber.Api.Media.Tests/CoreAudio/CoreAudioDeviceRepositoryTests.cs WinTabber.Api.Media.Tests/README.md .cleanup/tasks.md
git commit -m "chore: T5.5 7 — cover CoreAudioDeviceRepository's device-returning paths and Watch's volume/mute observables"
```

---

## Final verification

- [ ] `dotnet build WinTabber.slnx` — 0 warnings, 0 errors.
- [ ] `dotnet test --solution WinTabber.slnx` — 98 passed (baseline 92 + 6 new), 0 failed, 0
  skipped.
- [ ] `grep -rn "CoreAudioDeviceWrapper" --include=*.cs .` returns zero matches.
- [ ] `grep -rn "MMDevice[^E]" --include=*.cs .` returns matches only inside
  `WinTabber.Api.Media/CoreAudio/MMDeviceEnumeratorWrapper.cs` and
  `WinTabber.Api.Media/CoreAudio/Models/CoreAudioDevice.cs`.
- [ ] Manual smoke test: launch the app, open the audio device selector, change the default
  playback/recording device, and adjust volume/mute once each — this refactor touches every
  device-list and volume-control code path even though no test project can exercise the real
  WASAPI backend end-to-end.
