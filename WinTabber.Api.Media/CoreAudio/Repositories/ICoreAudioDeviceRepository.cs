using System.Reactive.Concurrency;
using DynamicData;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabber.Api.Media.CoreAudio.Repositories;

/// <summary>
/// The seam over <see cref="CoreAudioDeviceRepository"/>, extracted verbatim from its public
/// surface so consumers outside this assembly can be faked against it.
/// </summary>
/// <remarks>
/// <see cref="CoreAudioDeviceRepository.SetDefaultAudioEndpoint"/> is deliberately absent: it is
/// <c>internal</c>, and putting it here would mean widening its accessibility to satisfy the
/// interface — the opposite of what extracting a seam is for. Its only caller,
/// <see cref="Services.AudioDeviceService"/>, lives in this assembly and keeps the concrete
/// dependency for that reason.
/// </remarks>
public interface ICoreAudioDeviceRepository : IDisposable
{
    IScheduler Scheduler { get; }

    /// <summary>Generated from <c>GetDevices()</c> by the <c>[Lazy]</c> source generator.</summary>
    IObservableCache<IAudioDevice, string> Devices { get; }

    IObservableCache<DefaultDeviceChange, DefaultDeviceKey> GetDefaultDevices();

    IAudioDevice? GetDefaultPlaybackDevice();

    IAudioDevice? GetDefaultRecordingDevice();

    DeviceEvents Watch(IAudioDevice device);
}
