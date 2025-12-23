using CommunityToolkit.Mvvm.ComponentModel;
using CoreAudio;
using CoreAudio.Interfaces;
using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoreAudio.AudioSessionManager2;

namespace WinTabberUI.ViewModels;

public partial class DeviceItem : ObservableObject, IComparable<DeviceItem>, IEquatable<DeviceItem>
{
    public DeviceItem(MMDevice device, MMDeviceEnumerator enumerator)
    {
        Name = device.DeviceFriendlyName;
        Id = device.ID;
        Kind = device.DataFlow;
        _isSelected = device.Selected;
        _device = device;
        _enumerator = enumerator;


        Mute = ReactiveCommand.CreateFromObservable(MuteImpl, canExecute: null, RxApp.MainThreadScheduler);
        VolumeChanged.Subscribe(change =>
        {
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(Volume));

        });

        ////var sessions = new SourceCache<AudioSessionControl2, string>(session => session.SessionInstanceIdentifier);
        //if (device.AudioSessionManager2 is { } sessionManager)
        //{

        //    var sm = ObservableChangeSet.Create<AudioSessionControl2, string>(cache =>
        //    {
        //        cache.AddOrUpdate(sessionManager.Sessions?.ToArray() ?? []);
        //        sessionManager.Sessions[0].
        //        var sessionsAdded = Observable.FromEvent<SessionCreatedDelegate, IAudioSessionControl2>(
        //            handler =>
        //            {
        //                SessionCreatedDelegate rawHandler = (sender, newSession) =>
        //                {
        //                    newSession.ide
        //                    handler(newSession);
        //                };

        //                return rawHandler;
        //            },
        //            handler => sessionManager.OnSessionCreated += handler,
        //            handler => sessionManager.OnSessionCreated -= handler
        //        );

        //        var sessionsRemoved = Observable.FromEvent(
                
        //            handler => sessionManager.Sessions.s
        //        )

        //        return new CompositeDisposable();
        //    });

        //}


    }

    private IObservable<Unit> MuteImpl()
    {
        IsMuted = !IsMuted;
        return Observable.Return(Unit.Default);
    }

    public void Activate()
    {
        _enumerator.SetDefaultAudioEndpoint(_device);
    }


    [Lazy]
    private IObservable<AudioVolumeNotificationData> GetVolumeChanged()
    {
        return Observable.FromEvent<AudioEndpointVolumeNotificationDelegate, AudioVolumeNotificationData>(
            h =>
            {
                if (_device.AudioEndpointVolume is not null)
                    _device.AudioEndpointVolume.OnVolumeNotification += h;
            },
            h =>
            {
                if (_device.AudioEndpointVolume is not null)
                    _device.AudioEndpointVolume.OnVolumeNotification -= h;
            })
            .Publish()
            .RefCount();
    }
    public float Volume
    {
        get => _device.AudioEndpointVolume?.MasterVolumeLevelScalar ?? 0;
        set
        {
            if (_device.AudioEndpointVolume is null)
            {
                return;
            }
            _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)value;
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => _device.AudioEndpointVolume?.Mute ?? false;
        private set
        {
            if (_device.AudioEndpointVolume is null)
            {
                return;
            }
            _device.AudioEndpointVolume.Mute = value;
            OnPropertyChanged();
        }
    }

    public string Name { get; }

    public string Id { get; }
    public DataFlow Kind { get; }

    private readonly MMDevice _device;
    private readonly MMDeviceEnumerator _enumerator;

    public ReactiveCommand<Unit, Unit> Mute { get; }

    public MMDevice Device => _device;

    public bool IsSelected
    {
        get => _device.Selected;
        set
        {
            _isSelected = value;
            //_device.Selected = value;

            OnPropertyChanged();
        }
    }
    private bool _isSelected;

    public int CompareTo(DeviceItem? other)
    {
        return string.Compare(Name, other?.Name, StringComparison.Ordinal);
    }

    public bool Equals(DeviceItem? other)
    {
        return other?.Device.ID == Device.ID;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as DeviceItem);
    }

    public override int GetHashCode()
    {
        return Device.ID.GetHashCode();
    }

    public static bool operator ==(DeviceItem left, DeviceItem right)
    {
        if (ReferenceEquals(left, null))
        {
            return ReferenceEquals(right, null);
        }

        return left.Equals(right);
    }

    public static bool operator !=(DeviceItem left, DeviceItem right)
    {
        return !(left == right);
    }

    public static bool operator <(DeviceItem left, DeviceItem right)
    {
        return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
    }

    public static bool operator <=(DeviceItem left, DeviceItem right)
    {
        return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
    }

    public static bool operator >(DeviceItem left, DeviceItem right)
    {
        return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
    }

    public static bool operator >=(DeviceItem left, DeviceItem right)
    {
        return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
    }

    //public bool IsSelected
    //{
    //    get => _isSelected;
    //    set
    //    {
    //        _device.Selected = value;
    //        this.RaiseAndSetIfChanged(ref _isSelected, value);
    //    }
    //}
}