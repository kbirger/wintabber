using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using ReactiveUI;
using Windows.Foundation;
using Windows.Media.Control;
using WinTabber.Api.Media.CoreAudio.Models;

namespace WinTabberUI.ViewModels;

/// <summary>
/// Row types for the media debug window. They are flat and inert on purpose: the source objects are
/// COM wrappers whose members must not be touched from the render thread.
/// </summary>
public sealed record DeviceRow(string DeviceId, string DeviceName, string DeviceFriendlyName, string DataFlow);

/// <summary>
/// An SMTC session. This row is live, because playback status changes on the session itself and
/// never reaches the session list.
/// </summary>
/// <remarks>
/// The session list only changes when the session manager raises SessionsChanged, and that event
/// fires when a source appears or disappears, not when a source plays or pauses.
/// </remarks>
public sealed class SmtcSessionRow : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _cleanUp = new();
    private string _playbackStatus = string.Empty;

    public SmtcSessionRow(GlobalSystemMediaTransportControlsSession session, IScheduler comScheduler)
    {
        Aumid = session.SourceAppUserModelId;

        // Read the session on the COM scheduler, then move the plain string to the dispatcher.
        GetPlaybackInfoChanges(session)
            .ObserveOn(comScheduler)
            .Select(_ => ReadPlaybackStatus(session))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(status => PlaybackStatus = status)
            .DisposeWith(_cleanUp);
    }

    public string Aumid { get; }

    public string PlaybackStatus
    {
        get => _playbackStatus;
        private set => this.RaiseAndSetIfChanged(ref _playbackStatus, value);
    }

    public void Dispose()
    {
        _cleanUp.Dispose();
    }

    private static IObservable<Unit> GetPlaybackInfoChanges(GlobalSystemMediaTransportControlsSession session)
    {
        return Observable
            .FromEvent<TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>, Unit>(
                handler => (_, _) => handler(Unit.Default),
                handler => session.PlaybackInfoChanged += handler,
                handler => session.PlaybackInfoChanged -= handler
            )
            .StartWith(Unit.Default);
    }

    private static string ReadPlaybackStatus(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus.ToString() ?? "none";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }
}

public sealed record InstalledAppRow(string Aumid, string Name, string TargetPath, string PackageInstallPath);

public sealed record MasterSessionRow(string Key, string AppName, string Aumid, string NativeSession);

/// <summary>
/// A core audio session. This row is live, because display name, state and volume arrive as
/// separate observables after the session appears.
/// </summary>
public sealed class CoreAudioSessionRow : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _cleanUp = new();
    private string _displayName = string.Empty;
    private string _state = string.Empty;
    private bool _isMuted;
    private float _volume;

    public CoreAudioSessionRow(CoreAudioSessionWrapper session)
    {
        SessionId = session.Id;
        ProcessId = session.ProcessId;
        DeviceName = session.Device.FriendlyName;

        // The wrapper publishes on the STA audio thread. Move to the dispatcher before the values
        // reach a bound property.
        session
            .DisplayName.ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(name => DisplayName = name)
            .DisposeWith(_cleanUp);

        session
            .StateChanges.ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(state => State = state.ToString())
            .DisposeWith(_cleanUp);

        session
            .VolumeChanges.ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(volume =>
            {
                IsMuted = volume.IsMuted;
                Volume = volume.Volume;
            })
            .DisposeWith(_cleanUp);
    }

    public string SessionId { get; }
    public uint ProcessId { get; }
    public string DeviceName { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => this.RaiseAndSetIfChanged(ref _displayName, value);
    }

    public string State
    {
        get => _state;
        private set => this.RaiseAndSetIfChanged(ref _state, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => this.RaiseAndSetIfChanged(ref _isMuted, value);
    }

    public float Volume
    {
        get => _volume;
        private set => this.RaiseAndSetIfChanged(ref _volume, value);
    }

    public void Dispose()
    {
        _cleanUp.Dispose();
    }
}
