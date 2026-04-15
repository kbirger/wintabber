using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;

namespace WinTabber.Api.Media.CoreAudio.Models;

public class CoreAudioDeviceWrapper(MMDevice device, IScheduler scheduler)
{
    private readonly IScheduler _scheduler = scheduler;

    internal MMDevice Device { get; } = device;

    // Create properties for being able to safely access some fields without being on the right thread
    public DataFlow DataFlow { get; } = device.DataFlow;
    public string DisplayName { get; } = device.FriendlyName ?? device.DeviceFriendlyName;
    public string FriendlyName { get; } = device.FriendlyName ?? "Unknown Device";
    public string DeviceFriendlyName { get; } = device.DeviceFriendlyName;
    public bool CanSetVolume { get; } = device.AudioEndpointVolume.VolumeRange.MaxDecibels > device.AudioEndpointVolume.VolumeRange.MinDecibels;
    public bool CanMute { get; } = device.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Mute);
    public string Id { get; } = device.ID;



    internal IObservable<Unit> SetVolume(float volume)
    {
        return Observable.Start(() =>
        {
            if (Math.Abs(Device.AudioEndpointVolume.MasterVolumeLevelScalar - volume) > .01)
            {
                Device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
        }, _scheduler);
    }

    public IObservable<Unit> SetMute(bool isMuted)
    {
        return Observable.Start(() =>
        {
            if (Device.AudioEndpointVolume.Mute != isMuted)
            {
                Device.AudioEndpointVolume.Mute = isMuted;
            }
        }, _scheduler);
    }
}
