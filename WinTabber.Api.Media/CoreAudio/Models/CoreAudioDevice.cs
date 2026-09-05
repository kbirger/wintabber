using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class CoreAudioDevice(MMDevice device, IScheduler scheduler) : IAudioDevice
{
    private readonly MMDevice _device = device;
    private readonly IScheduler _scheduler = scheduler;

    // Create properties for being able to safely access some fields without being on the right thread
    public string Id { get; } = device.ID;
    public DataFlow DataFlow { get; } = device.DataFlow;
    public string DisplayName { get; } = device.FriendlyName ?? device.DeviceFriendlyName;
    public string FriendlyName { get; } = device.FriendlyName ?? "Unknown Device";
    public string DeviceFriendlyName { get; } = device.DeviceFriendlyName;
    public bool CanSetVolume { get; } =
        device.AudioEndpointVolume.VolumeRange.MaxDecibels > device.AudioEndpointVolume.VolumeRange.MinDecibels;
    public bool CanMute { get; } = device.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Mute);
    public DeviceState State { get; } = device.State;

    public float MasterVolumeLevelScalar
    {
        get => _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        set => _device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
    }

    public bool Mute
    {
        get => _device.AudioEndpointVolume.Mute;
        set => _device.AudioEndpointVolume.Mute = value;
    }

    public AudioSessionManager AudioSessionManager => _device.AudioSessionManager;

    public IObservable<(float MasterVolume, bool Muted)> VolumeChanged =>
        Observable
            .Defer(() =>
            {
                var audioEndpointVolume = _device.AudioEndpointVolume;
                return Observable.FromEvent<AudioEndpointVolumeNotificationDelegate, AudioVolumeNotificationData>(
                    h =>
                    {
                        if (audioEndpointVolume is not null)
                            audioEndpointVolume.OnVolumeNotification += h;
                    },
                    h =>
                    {
                        if (audioEndpointVolume is not null)
                            audioEndpointVolume.OnVolumeNotification -= h;
                    }
                );
            })
            .Select(change => (change.MasterVolume, change.Muted))
            .SubscribeOn(_scheduler);

    public IObservable<Unit> SetVolume(float volume)
    {
        return Observable.Start(() =>
        {
            if (Math.Abs(_device.AudioEndpointVolume.MasterVolumeLevelScalar - volume) > .01)
            {
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
        }, _scheduler);
    }

    public IObservable<Unit> SetMute(bool isMuted)
    {
        return Observable.Start(() =>
        {
            if (_device.AudioEndpointVolume.Mute != isMuted)
            {
                _device.AudioEndpointVolume.Mute = isMuted;
            }
        }, _scheduler);
    }
}
