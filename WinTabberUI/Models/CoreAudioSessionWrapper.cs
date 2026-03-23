//using CommunityToolkit.Mvvm.ComponentModel;
//using NAudio.CoreAudioApi;
//using NAudio.CoreAudioApi.Interfaces;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows.Threading;

//namespace WinTabberUI.Models;

//public class CoreAudioSessionWrapper : ObservableObject, IAudioSessionEventsHandler, IDisposable
//{
//    private readonly AudioSessionControl _nativeSession;
//    private readonly CoreAudioDeviceWrapper _device;
//    private bool _disposed = false;
//    private Dispatcher Dispatcher { get; }

//    public CoreAudioSessionWrapper(AudioSessionControl nativeSession, CoreAudioDeviceWrapper device)
//    {
//        Dispatcher = Dispatcher.CurrentDispatcher;
//        _nativeSession = nativeSession;
//        _device = device;
//        _nativeSession.RegisterEventClient(this);
//    }

//    public void Dispose()
//    {
//        try
//        {
//            _nativeSession.UnRegisterEventClient(this);
//            _nativeSession.Dispose();
//            _disposed = true;
//        }
//        catch
//        {
//            // Ignore exceptions during dispose
//        }
//    }

//    ~CoreAudioSessionWrapper()
//    {
//        if (!_disposed)
//        {
//            Dispose();
//        }
//    }

//    public CoreAudioDeviceWrapper Device => _device;

//    public AudioSessionState State => _nativeSession.State;

//    public string DisplayName => _nativeSession.DisplayName;

//    public string IconPath => _nativeSession.IconPath;

//    public string SessionIdentifier => _nativeSession.GetSessionIdentifier;

//    public string SessionInstanceIdentifier => _nativeSession.GetSessionInstanceIdentifier;

//    public uint ProcessId => _nativeSession.GetProcessID;

//    public bool IsSystemSoundsSession => _nativeSession.IsSystemSoundsSession;

//    public float Volume
//    {
//        get => _nativeSession.SimpleAudioVolume.Volume;
//        private set => _nativeSession.SimpleAudioVolume.Volume = value;
//    }

//    public bool IsMuted
//    {
//        get => _nativeSession.SimpleAudioVolume.Mute;
//        private set => _nativeSession.SimpleAudioVolume.Mute = value;
//    }

//    public void SetVolume(float volume)
//    {
//        Volume = volume;
//        Dispatcher.BeginInvoke(() =>
//        {
//            _nativeSession.SimpleAudioVolume.Volume = volume;
//        });
//    }

//    public void SetMute(bool isMuted)
//    {
//        IsMuted = IsMuted;
//        Dispatcher.BeginInvoke(() =>
//        {
//            _nativeSession.SimpleAudioVolume.Mute = isMuted;
//        });
//    }

//    public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
//    {
//        Debug.WriteLine("Channel volume changed");
//        //OnPropertyChanged(nameof(Volume));
//    }

//    public void OnDisplayNameChanged(string displayName)
//    {
//        Debug.WriteLine("DisplayName changed");

//        OnPropertyChanged(nameof(DisplayName));
//    }

//    public void OnGroupingParamChanged(ref Guid groupingId)
//    {
        
//    }

//    public void OnIconPathChanged(string iconPath)
//    {
//        Debug.WriteLine("Icon path changed");

//        OnPropertyChanged(nameof(IconPath));
//    }

//    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
//    {
//        Debug.WriteLine("Session disconnected");


//    }

//    public void OnStateChanged(AudioSessionState state)
//    {
//        Debug.WriteLine($"Session state changed: {this.SessionIdentifier} {state}");
//        OnPropertyChanged(nameof(State));
//    }

//    public void OnVolumeChanged(float volume, bool isMuted)
//    {
//        Debug.WriteLine($"volume changed");
//        OnPropertyChanged(nameof(Volume));
//    }
//}
