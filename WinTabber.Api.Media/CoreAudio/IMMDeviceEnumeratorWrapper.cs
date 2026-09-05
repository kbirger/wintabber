using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

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
