using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinTabber.Api.Media.CoreAudio;

namespace WinTabber.Api.Media.Tests.Fakes;

/// <summary>
/// Hand-rolled fake for <see cref="IMMDeviceEnumeratorWrapper"/>. Only
/// <see cref="HasDefaultAudioEndpoint"/> and the endpoint-notification-callback methods are
/// implemented for real: NAudio's <see cref="MMDevice"/> has only an internal constructor taking
/// an internal COM interface, so no test code can construct one — the three device-returning
/// members (<see cref="GetDefaultAudioEndpoint"/>, <see cref="EnumerateAudioEndPoints"/>,
/// <see cref="GetDevice"/>) throw <see cref="NotSupportedException"/> so accidental use in a test
/// that would need a real device fails loudly instead of silently returning null/garbage.
/// </summary>
public sealed class FakeMMDeviceEnumeratorWrapper : IMMDeviceEnumeratorWrapper
{
    private readonly HashSet<(DataFlow Flow, Role Role)> _defaultEndpoints = [];
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

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) => RegisteredCallbacks.Add(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) => UnregisteredCallbacks.Add(client);

    public void Dispose() { }

    // ── Cannot be faked: MMDevice has no accessible constructor ────────────

    public MMDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role) =>
        throw new NotSupportedException("MMDevice cannot be constructed by test code (internal constructor).");

    public IEnumerable<MMDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState) =>
        throw new NotSupportedException("MMDevice cannot be constructed by test code (internal constructor).");

    public MMDevice GetDevice(string id) =>
        throw new NotSupportedException("MMDevice cannot be constructed by test code (internal constructor).");
}
