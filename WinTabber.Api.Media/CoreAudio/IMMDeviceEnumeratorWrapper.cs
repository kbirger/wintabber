using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WinTabber.Api.Media.CoreAudio;

public interface IMMDeviceEnumeratorWrapper : IDisposable
{
    MMDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role);
    bool HasDefaultAudioEndpoint(DataFlow dataFlow, Role role);
    IEnumerable<MMDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState);
    MMDevice GetDevice(string id);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}
