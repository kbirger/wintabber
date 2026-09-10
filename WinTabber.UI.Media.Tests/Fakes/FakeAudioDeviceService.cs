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
/// them. Only the three members <see cref="ViewModels.AudioDeviceSelectorViewModel"/> uses are
/// implemented; the rest throw, so a future call site that starts depending on them fails loudly
/// here instead of silently getting a default.
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

    public ObservableDeviceDto WatchDevice(IAudioDevice? device) => throw new NotSupportedException();

    public IObservable<ObservableDeviceDto> WatchDevice(string deviceId) => throw new NotSupportedException();

    public IObservable<Unit> SetVolume(string deviceId, float volume) => throw new NotSupportedException();

    public IObservable<Unit> SetMute(string deviceId, bool isMuted) => throw new NotSupportedException();
}
