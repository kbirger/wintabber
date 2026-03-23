using System;
using System.Collections.Generic;
using System.Text;

namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class DeviceDto
{
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public required string DeviceFriendlyName { get; init; }

    public required bool IsSelected { get; init; }
}
