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
