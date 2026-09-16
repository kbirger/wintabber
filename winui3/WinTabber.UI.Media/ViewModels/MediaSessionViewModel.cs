using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private readonly ObservableAsPropertyHelper<BitmapImage?> _thumbnail;

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

        // Replay(1), not Replay(): an unbounded replay hands every late subscriber the whole
        // history of sessions, and each one builds a monitor for every past session.
        SessionChanged = this.WhenAnyValue(vm => vm.Session)
            .Log(x => $"Session changed before distinct: {x?.Key} - {x?.NativeSession != null}")
            .Replay(1)
            .RefCount();

        var scheduler = RxSchedulers.MainThreadScheduler;
        var deviceSession = SessionChanged.Select(session => new ObservableSessionDto(session?.NativeSession));
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

        // ObserveOn before SelectMany, not after (the reverse of the WPF original's order):
        // BitmapImage.SetSourceAsync is DispatcherQueue-affine in WinUI 3, unlike WPF's
        // stream-based BitmapImage construction, which is safe off the UI thread. The decode must
        // already be on scheduler before it runs.
        _thumbnail = monitors
            .Select(monitor => monitor?.ThumbnailChanges)
            .OrDefault<IRandomAccessStreamReference?>(null)
            .Switch()
            .ObserveOn(scheduler)
            .SelectMany(GetCurrentMediaAlbumArt)
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
    public BitmapImage? Thumbnail => _thumbnail?.Value;

    public static async Task<BitmapImage?> GetCurrentMediaAlbumArt(IRandomAccessStreamReference? imageStream)
    {
        if (imageStream is null)
        {
            return null;
        }

        using IRandomAccessStreamWithContentType streamRef = await imageStream.OpenReadAsync();
        var imageSource = new BitmapImage();
        await imageSource.SetSourceAsync(streamRef);
        return imageSource;
    }

    public void Dispose()
    {
        _disposable.Dispose();
    }
}
