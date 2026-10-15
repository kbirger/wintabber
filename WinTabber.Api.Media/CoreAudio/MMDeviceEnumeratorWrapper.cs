using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
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

    // CoreAudioDevice reads AudioEndpointVolume in its constructor, which fails for a device that
    // disappears or is not yet usable (for example the redirected device of a remote desktop
    // session). Skip such a device so that one bad device does not fail the whole enumeration.
    public IEnumerable<IAudioDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState)
    {
        foreach (var device in _enumerator.EnumerateAudioEndPoints(dataFlow, deviceState))
        {
            CoreAudioDevice? wrapped;
            try
            {
                wrapped = new CoreAudioDevice(device, _scheduler);
            }
            catch (Exception ex) when (ex is COMException or FileNotFoundException)
            {
                Debug.WriteLine($"Skipped audio device during enumeration: {ex.Message}");
                continue;
            }

            yield return wrapped;
        }
    }

    public IAudioDevice GetDevice(string id) => new CoreAudioDevice(_enumerator.GetDevice(id), _scheduler);

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.RegisterEndpointNotificationCallback(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.UnregisterEndpointNotificationCallback(client);

    public void Dispose() => _enumerator.Dispose();
}
