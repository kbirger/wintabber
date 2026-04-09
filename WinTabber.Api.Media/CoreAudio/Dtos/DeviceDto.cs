using NAudio.CoreAudioApi;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class DeviceDto : IEquatable<DeviceDto>
{
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public required string DeviceFriendlyName { get; init; }

    //public required bool IsSelected { get; init; }

    public required DataFlow DataFlow { get; init; }

    public override bool Equals(object? obj)
    {
        return base.Equals(obj as DeviceDto);
    }
    public bool Equals(DeviceDto? other)
    {
        return string.Equals(other?.DeviceId, DeviceId, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        return DeviceId.GetHashCode();
    }
}
