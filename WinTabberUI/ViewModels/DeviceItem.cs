//using CommunityToolkit.Mvvm.ComponentModel;
//using DynamicData;
//using NAudio.CoreAudioApi;
//using ReactiveUI;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Reactive;
//using System.Reactive.Disposables;
//using System.Reactive.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Windows.Win32;
//using WinTabberUI.Interop;
//using WinTabberUI.Models;

//namespace WinTabberUI.ViewModels;

//public partial class DeviceItem : ObservableObject, IComparable<DeviceItem>, IEquatable<DeviceItem>
//{
//    private static PolicyConfigClient _policy = new PolicyConfigClient();

//    public DeviceItem(CoreAudioDeviceWrapper device)
//    {
//        Name = !string.IsNullOrWhiteSpace(device.DeviceFriendlyName) ? 
//            device.DeviceFriendlyName : 
//            device.FriendlyName;
//        Id = device.ID;
//        Kind = device.DataFlow;
//        _isSelected = device.IsDefault;
//        _device = device;


//        Mute = ReactiveCommand.CreateFromObservable(MuteImpl, canExecute: null, RxSchedulers.MainThreadScheduler);
//        //VolumeChanged.Subscribe(change =>
//        //{
//        //    OnPropertyChanged(nameof(IsMuted));
//        //    OnPropertyChanged(nameof(Volume));

//        //});


//        ////var sessions = new SourceCache<AudioSessionControl2, string>(session => session.SessionInstanceIdentifier);
//        //if (device.AudioSessionManager2 is { } sessionManager)
//        //{

//        //    var sm = ObservableChangeSet.Create<AudioSessionControl2, string>(cache =>
//        //    {
//        //        cache.AddOrUpdate(sessionManager.Sessions?.ToArray() ?? []);
//        //        sessionManager.Sessions[0].
//        //        var sessionsAdded = Observable.FromEvent<SessionCreatedDelegate, IAudioSessionControl2>(
//        //            handler =>
//        //            {
//        //                SessionCreatedDelegate rawHandler = (sender, newSession) =>
//        //                {
//        //                    newSession.ide
//        //                    handler(newSession);
//        //                };

//        //                return rawHandler;
//        //            },
//        //            handler => sessionManager.OnSessionCreated += handler,
//        //            handler => sessionManager.OnSessionCreated -= handler
//        //        );

//        //        var sessionsRemoved = Observable.FromEvent(
                
//        //            handler => sessionManager.Sessions.s
//        //        )

//        //        return new CompositeDisposable();
//        //    });

//        //}


//    }

//    private IObservable<Unit> MuteImpl()
//    {
//        IsMuted = !IsMuted;
//        return Observable.Return(Unit.Default);
//    }

//    public void Activate()
//    {
//        try
//        {

//            _policy.SetDefaultEndpoint(Id, Role.Multimedia);
//            _policy.SetDefaultEndpoint(Id, Role.Communications);
//            _policy.SetDefaultEndpoint(Id, Role.Console);
//            //_enumerator.EnumerateAudioEndPoints(Kind, _device)[0].AudioSessionManager;
//        }
//        catch (Exception ex)
//        {
//            Debug.WriteLine($"Failed to set default endpoint: {ex}");
//        }
//    }

    
//    public float Volume
//    {
//        get => _device.Volume;
//        set
//        {
//            if (_device.Device.AudioEndpointVolume is null)
//            {
//                return;
//            }
//            _device.Device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)value;
//            OnPropertyChanged();
//        }
//    }

//    public bool IsMuted
//    {
//        get => _device.IsMuted;
//        private set
//        {
//            if (_device.Device.AudioEndpointVolume is null)
//            {
//                return;
//            }
//            _device.Device.AudioEndpointVolume.Mute = value;
//            OnPropertyChanged();
//        }
//    }

//    public string Name { get; }

//    public string Id { get; }
//    public DataFlow Kind { get; }

//    private readonly CoreAudioDeviceWrapper _device;

//    public ReactiveCommand<Unit, Unit> Mute { get; }

//    public MMDevice Device => _device.Device;

//    public bool IsSelected
//    {
//        get => _device.IsDefault;
//        private set
//        {
//            // todo: this doesn't make sense, we should be setting the default device here
//            // and then updating the property based on that, not setting the property and then trying to set the default device based on that
//            _isSelected = value;
//            //_device.Selected = value;

//            OnPropertyChanged();
//        }
//    }
//    private bool _isSelected;

//    public int CompareTo(DeviceItem? other)
//    {
//        return string.Compare(Name, other?.Name, StringComparison.Ordinal);
//    }

//    public bool Equals(DeviceItem? other)
//    {
//        return other?.Id == Id;
//    }

//    public override bool Equals(object obj)
//    {
//        return Equals(obj as DeviceItem);
//    }

//    public override int GetHashCode()
//    {
//        return Id.GetHashCode();
//    }

//    public static bool operator ==(DeviceItem left, DeviceItem right)
//    {
//        if (ReferenceEquals(left, null))
//        {
//            return ReferenceEquals(right, null);
//        }

//        return left.Equals(right);
//    }

//    public static bool operator !=(DeviceItem left, DeviceItem right)
//    {
//        return !(left == right);
//    }

//    public static bool operator <(DeviceItem left, DeviceItem right)
//    {
//        return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
//    }

//    public static bool operator <=(DeviceItem left, DeviceItem right)
//    {
//        return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
//    }

//    public static bool operator >(DeviceItem left, DeviceItem right)
//    {
//        return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
//    }

//    public static bool operator >=(DeviceItem left, DeviceItem right)
//    {
//        return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
//    }

//    //public bool IsSelected
//    //{
//    //    get => _isSelected;
//    //    set
//    //    {
//    //        _device.Selected = value;
//    //        this.RaiseAndSetIfChanged(ref _isSelected, value);
//    //    }
//    //}
//}