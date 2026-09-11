using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using NAudio.CoreAudioApi;
using WinTabber.Api.Media.CoreAudio;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;

namespace WinTabber.UI.Media.Tests.Fakes;

/// <summary>
/// Counts live subscriptions so a test can assert that disposing a view model actually released
/// them. Only the members <see cref="ViewModels.AudioDeviceSelectorViewModel"/> and
/// <see cref="ViewModels.MediaSessionViewModel"/> use are implemented; the rest throw, so a
/// future call site that starts depending on one fails loudly here instead of silently getting a
/// default.
/// </summary>
public sealed class FakeAudioDeviceService : IAudioDeviceService
{
    private readonly SourceCache<DeviceDto, string> _devices = new(d => d.DeviceId);

    public FakeAudioDeviceService(params DeviceDto[] devices) => _devices.AddOrUpdate(devices);

    public int LiveDefaultDeviceSubscriptions { get; private set; }

    public int LiveEndpointSubscriptions { get; private set; }

    public IObservableCache<DeviceDto, string> Devices => _devices.AsObservableCache();

    public IObservable<DeviceDto> GetDefaultDevice(
        DataFlow dataFlow = DataFlow.All,
        Role role = Role.Multimedia
    ) =>
        Observable.Create<DeviceDto>(_ =>
        {
            LiveDefaultDeviceSubscriptions++;
            return Disposable.Create(() => LiveDefaultDeviceSubscriptions--);
        });

    /// <summary>Never completes, so a subscription stays live until it is disposed.</summary>
    public IObservable<Unit> SetDefaultAudioEndpoint(string deviceId) =>
        Observable.Create<Unit>(_ =>
        {
            LiveEndpointSubscriptions++;
            return Disposable.Create(() => LiveEndpointSubscriptions--);
        });

    public IObservable<Unit> SetDefaultAudioEndpoint(string deviceId, params Role[] roles) =>
        SetDefaultAudioEndpoint(deviceId);

    /// <summary>
    /// Returns an inert DTO: <see cref="ViewModels.MediaSessionViewModel"/> only needs
    /// <em>something</em> non-null to build its volume-control pipelines against, never a real
    /// device.
    /// </summary>
    public ObservableDeviceDto WatchDevice(IAudioDevice? device) =>
        new()
        {
            DisplayName = "",
            Id = "",
            CanSetVolume = false,
            CanMute = false,
            StateChanges = Observable.Never<DeviceState>(),
            Removed = Observable.Never<Unit>(),
            PropertyChanges = Observable.Never<PropertyKey>(),
            IsDefaultChanges = Observable.Never<bool>(),
            VolumeChanges = Observable.Never<float>(),
            IsMutedChanges = Observable.Never<bool>(),
            CanMuteChanges = Observable.Never<bool>(),
            CanSetVolumeChanges = Observable.Never<bool>(),
            SetVolume = _ => Observable.Empty<Unit>(),
            SetMute = _ => Observable.Empty<Unit>(),
        };

    public IObservable<ObservableDeviceDto> WatchDevice(string deviceId) => throw new NotSupportedException();

    public IObservable<Unit> SetVolume(string deviceId, float volume) => throw new NotSupportedException();

    public IObservable<Unit> SetMute(string deviceId, bool isMuted) => throw new NotSupportedException();
}
