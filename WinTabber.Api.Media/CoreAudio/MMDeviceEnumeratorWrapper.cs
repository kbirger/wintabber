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
            .Select(device => new CoreAudioDevice(device, _scheduler));

    public IAudioDevice GetDevice(string id) => new CoreAudioDevice(_enumerator.GetDevice(id), _scheduler);

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.RegisterEndpointNotificationCallback(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.UnregisterEndpointNotificationCallback(client);

    public void Dispose() => _enumerator.Dispose();
}
