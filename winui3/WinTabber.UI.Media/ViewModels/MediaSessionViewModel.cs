using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Windows.Storage.Streams;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.SMTC.Services;
using WinTabber.Common.Util;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.ViewModels;

public partial class MediaSessionViewModel : ReactiveObject, IDisposable
{
    private readonly ObservableAsPropertyHelper<string> _artistName;
    private readonly ObservableAsPropertyHelper<string> _albumTitle;
    private readonly ObservableAsPropertyHelper<string> _title;
    private readonly ObservableAsPropertyHelper<byte[]?> _thumbnail;

    private readonly IAudioSessionService _sessionService;
    private readonly IAudioDeviceService _deviceService;

    public IObservable<AggregateSession?> SessionChanged { get; }

    private readonly CompositeDisposable _disposable = new CompositeDisposable();

    // Do not pre-assign the backing field. RaiseAndSetIfChanged compares the field with the new
    // value and returns without a notification when they are equal. An assignment before the call
    // makes that comparison always true, so PropertyChanged never fires and SessionChanged stalls.
    public AggregateSession? Session
    {
        get => field;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public MediaSessionViewModel(IAudioSessionService audioSessionService, IAudioDeviceService audioDeviceService)
    {
        _sessionService = audioSessionService;
        _deviceService = audioDeviceService;

        var scheduler = RxSchedulers.MainThreadScheduler;

        // Replay(1), not Replay(): an unbounded replay hands every late subscriber the whole
        // history of sessions, and each one builds a monitor for every past session.
        //
        // Merged with NativeSessionChanged, not just WhenAnyValue(vm => vm.Session) alone: a device
        // switch mutates the SAME AggregateSession instance in place (UpdateNativeSession), and its
        // Key/Equals deliberately ignore NativeSession, so RaiseAndSetIfChanged on Session never
        // fires for that mutation. Confirmed live on the WinUI 3 app; this file is shared, so the
        // WPF app runs the same code. The device volume slider (DeviceVolumeControls, built from
        // SessionChanged below) updated correctly on the first device switch after a session was
        // selected, then froze on every switch after that, because Session's own reference never
        // changed again. NativeSessionChanged is the session's own notification of that mutation,
        // independent of Session's reference-based change detection.
        //
        // ObserveOn(scheduler) here, not left to each downstream consumer: NativeSessionChanged
        // fires from AggregateSession.UpdateNativeSession, called from GetMasterSessions's
        // .ObserveOn(staScheduler) pipeline -- the COM STA thread, not the UI thread. Confirmed
        // live on the WinUI 3 app: without this, monitors.Subscribe(monitor => Playback.Session =
        // monitor) ran on that STA thread and crashed with a COMException while WinUI 3 marshaled
        // the WinRT PropertyChangedEventArgs off the UI thread. WPF's PropertyChangedEventArgs has
        // no such thread affinity, so the crash does not reproduce on the WPF app, but both apps
        // share this file and every downstream consumer of SessionChanged relies on the same
        // thread-affinity contract (see AudioDeviceSelectorViewModel's own comment on the same
        // hazard).
        SessionChanged = this.WhenAnyValue(vm => vm.Session)
            .Select(session =>
                session == null
                    ? Observable.Return(session)
                    : Observable.Return(session).Concat(session.NativeSessionChanged.Select(_ => session))
            )
            .Switch()
            .ObserveOn(scheduler)
            .Log(x => $"Session changed before distinct: {x?.Key} - {x?.NativeSession != null}")
            .Replay(1)
            .RefCount();
        var deviceSession = SessionChanged.Select(session => new ObservableSessionDto(session?.NativeSession));
        // todo: this is incorrect. need device
        var device = SessionChanged.Select(session => audioDeviceService.WatchDevice(session?.NativeSession?.Device));

        // Replay(1).RefCount() is required, not decoration. The projection builds a monitor, and
        // five places subscribe to it. Without sharing, each subscriber built its own monitor, so
        // every session change created five monitors with five sets of live event handlers and
        // five one-second timers, and the view model drove only the last of them.
        var monitors = SessionChanged
            .Select(session => session is null ? null : new SMTCSessionMonitor(session.MediaSession))
            .Replay(1)
            .RefCount();
        _artistName = monitors
            .Select(monitor => monitor?.ArtistNameChanges)
            .OrDefault("")
            .Switch()
            .ToProperty(this, vm => vm.ArtistName, initialValue: "")
            .DisposeWith(_disposable);

        _albumTitle = monitors
            .Select(monitor => monitor?.AlbumTitleChanges)
            .OrDefault("")
            .Switch()
            .ToProperty(this, vm => vm.AlbumTitle, initialValue: "")
            .DisposeWith(_disposable);

        _title = monitors
            .Select(monitor => monitor?.TitleChanges)
            .OrDefault("")
            .Switch()
            .ToProperty(this, vm => vm.Title, initialValue: "")
            .DisposeWith(_disposable);

        _thumbnail = monitors
            .Select(monitor => monitor?.ThumbnailChanges)
            .OrDefault<IRandomAccessStreamReference?>(null)
            .Switch()
            .SelectMany(ReadAlbumArtBytes)
            .ObserveOn(scheduler)
            .ToProperty(this, vm => vm.Thumbnail, initialValue: null)
            .DisposeWith(_disposable);

        _thumbnail
            .ThrownExceptions.Subscribe(ex =>
            {
                Debug.WriteLine("Error retrieving thumbnail: {0}", ex);
            })
            .DisposeWith(_disposable);

        Playback = new PlaybackControlsViewModel(scheduler);
        monitors
            .Subscribe(monitor =>
            {
                Playback.Session = monitor;
            })
            .DisposeWith(_disposable);

        DeviceVolumeControls = new VolumeControlsViewModel(device, volumeHintText: "DV", muteHintText: "DM");

        SessionVolumeControls = new VolumeControlsViewModel(deviceSession, volumeHintText: "PM", muteHintText: "PM");

        Observable
            .Merge(SessionVolumeControls.ThrownExceptions, DeviceVolumeControls.ThrownExceptions)
            .Subscribe(ex =>
            {
                Debug.WriteLine("Error processing media keys");
                Debug.WriteLine(ex);
            });
    }

    public VolumeControlsViewModel DeviceVolumeControls { get; }
    public VolumeControlsViewModel SessionVolumeControls { get; }

    public PlaybackControlsViewModel Playback { get; }

    public string ArtistName => _artistName.Value;
    public string AlbumTitle => _albumTitle.Value;
    public string Title => _title.Value;
    public byte[]? Thumbnail => _thumbnail?.Value;

    // Framework-neutral: reads the SMTC thumbnail stream into a byte[] payload. Decoding those
    // bytes into a UI-framework-specific ImageSource happens in ImageBytesToImageSourceConverter
    // (WinTabber.UI.Common) on the UI thread during binding, not here.
    public static async Task<byte[]?> ReadAlbumArtBytes(IRandomAccessStreamReference? imageStream)
    {
        if (imageStream is null)
        {
            return null;
        }

        using IRandomAccessStreamWithContentType streamRef = await imageStream.OpenReadAsync();
        using var inputStream = streamRef.AsStreamForRead();
        using var memoryStream = new MemoryStream();
        await inputStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    public void Dispose()
    {
        _disposable.Dispose();
    }
}
