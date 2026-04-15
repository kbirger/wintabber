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
    public readonly DataFlow DataFlow = device.DataFlow;
    public string DisplayName = device.FriendlyName ?? device.DeviceFriendlyName;
    public string FriendlyName = device.FriendlyName ?? "Unknown Device";
    public string DeviceFriendlyName = device.DeviceFriendlyName;

    public string Id = device.ID;



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
