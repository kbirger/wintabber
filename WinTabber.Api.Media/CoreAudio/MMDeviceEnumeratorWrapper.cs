using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WinTabber.Api.Media.CoreAudio;

public sealed class MMDeviceEnumeratorWrapper : IMMDeviceEnumeratorWrapper, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public MMDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role) =>
        _enumerator.GetDefaultAudioEndpoint(dataFlow, role);

    public bool HasDefaultAudioEndpoint(DataFlow dataFlow, Role role) =>
        _enumerator.HasDefaultAudioEndpoint(dataFlow, role);

    public IEnumerable<MMDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState) =>
        _enumerator.EnumerateAudioEndPoints(dataFlow, deviceState);

    public MMDevice GetDevice(string id) =>
        _enumerator.GetDevice(id);

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.RegisterEndpointNotificationCallback(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.UnregisterEndpointNotificationCallback(client);

    public void Dispose() => _enumerator.Dispose();
}
