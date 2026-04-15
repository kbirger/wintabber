using ReactiveUI;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.SMTC.Services;
using WinTabber.Interop;
using WinTabber.UI.Media.Models;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.UI.Media.ViewModels;

public class MediaSessionViewModel : ReactiveObject, IDisposable
{
    private readonly ObservableAsPropertyHelper<string> _artistName;
    private readonly ObservableAsPropertyHelper<string> _albumTitle;
    private readonly ObservableAsPropertyHelper<string> _title;
    private readonly ObservableAsPropertyHelper<ImageSource?> _thumbnail;
    //private readonly ObservableAsPropertyHelper<TimeSpan> _duration;
    //private readonly ObservableAsPropertyHelper<TimeSpan> _position;
    //private readonly ObservableAsPropertyHelper<float> _progress;
    //private readonly ObservableAsPropertyHelper<bool> _isPlaying;
    //private readonly ObservableAsPropertyHelper<bool> _canSeek;

    private readonly AggregateSession _session;
    private readonly AudioSessionService _sessionService;
    private readonly AudioDeviceService _deviceService;
    private readonly CompositeDisposable _disposable = new CompositeDisposable();

    //private readonly ObservableSess ionDto? _deviceSession;
    //private readonly ObservableDeviceDto? _device;

    public MediaSessionViewModel(
        AggregateSession session,
        AudioSessionService audioSessionService,
        AudioDeviceService audioDeviceService
    )
    {
        _session = session;
        _sessionService = audioSessionService;
        _deviceService = audioDeviceService;
        var scheduler = RxSchedulers.MainThreadScheduler;
        var hasNativeSession = session.NativeSession is not null;
        var smtcSession = session.MediaSession;
        var deviceSession = session.NativeSession is null ? new ObservableSessionDto() : new ObservableSessionDto(session.NativeSession);
        // todo: this is incorrect. need device
        var device = audioDeviceService.WatchDevice(session.NativeSession?.Device);
        //var device = session.NativeSession is null
        //    ? Observable.Empty<ObservableDeviceDto>()
        //    : audioDeviceService.WatchDevice(session.NativeSession.Device).FirstAsync().ObserveOn(scheduler);
        //var mediaPropertiesChanged = ObserveMediaProperties(smtcSession);
        //var playbackPropertiesChanged = ObservePlaybackProperties(smtcSession);
        //var timelinePropertyChanged = ObserveTimelineProperties(smtcSession);

        var monitor = new SMTCSessionMonitor(smtcSession);
        _artistName = monitor
            .ArtistNameChanges
            //.Do(t => Debug.WriteLine($"artist {t}"))
            .ToProperty(this, vm => vm.ArtistName, initialValue: "")
            .DisposeWith(_disposable);

        _albumTitle = monitor
            .AlbumTitleChanges.ToProperty(this, vm => vm.AlbumTitle, initialValue: "")
            .DisposeWith(_disposable);

        _title = monitor.TitleChanges.ToProperty(this, vm => vm.Title, initialValue: "").DisposeWith(_disposable);

        _thumbnail = monitor
            .ThumbnailChanges.ObserveOn(scheduler)
            .ToProperty(this, vm => vm.Thumbnail, initialValue: null)
            .DisposeWith(_disposable);

        _thumbnail
            .ThrownExceptions.Subscribe(ex =>
            {
                Debug.WriteLine("Error retrieving thumbnail: {0}", ex);
            })
            .DisposeWith(_disposable);

        //_isPlaying = monitor
        //    .IsPlayingChanges
        //    .ToProperty(this, vm => vm.IsPlaying)
        //    .DisposeWith(_disposable);

        //_canSeek = monitor.CanSeekChanges.ToProperty(this, vm => vm.CanSeek).DisposeWith(_disposable);

        //_duration = monitor.DurationChanges.ToProperty(this, vm => vm.Duration).DisposeWith(_disposable);



        //PlayPause = ReactiveCommand
        //    .CreateFromObservable(PlayPauseImpl, canExecute: monitor.CanPlayPauseChanges, outputScheduler: scheduler)
        //    .DisposeWith(_disposable);
        //Next = ReactiveCommand
        //    .CreateFromObservable(NextImpl, canExecute: monitor.CanNextChanges, outputScheduler: scheduler)
        //    .DisposeWith(_disposable);
        //Prev = ReactiveCommand
        //    .CreateFromObservable(PrevImpl, canExecute: monitor.CanPrevChanges, outputScheduler: scheduler)
        //    .DisposeWith(_disposable);

        //Mute = ReactiveCommand
        //    .CreateFromObservable(MuteImpl, canExecute: null, outputScheduler: scheduler)
        //    .DisposeWith(_disposable);

        //Pause = ReactiveCommand
        //    .CreateFromObservable(PauseImpl, canExecute: monitor.CanPauseChanges, outputScheduler: scheduler)
        //    .DisposeWith(_disposable);

        //Seek = ReactiveCommand
        //    .CreateFromObservable<TimeSpan, Unit>(
        //        SeekImpl,
        //        canExecute: monitor.CanSeekChanges,
        //        outputScheduler: scheduler
        //    )
        //    .DisposeWith(_disposable);


        Playback = new PlaybackControlsViewModel(scheduler);
        Playback.Session = monitor;
        DeviceVolumeControls = new VolumeControlsViewModel(
            device,
            volumeHintText: "V",
            muteHintText: "M"
        );

        SessionVolumeControls = new VolumeControlsViewModel(
            deviceSession,
            volumeHintText: "U",
            muteHintText: "X"
        );

        Observable
            .Merge(
                SessionVolumeControls.ThrownExceptions,
                DeviceVolumeControls.ThrownExceptions
            )
            .Subscribe(ex =>
            {
                Debug.WriteLine("Error processing media keys");
                Debug.WriteLine(ex);
            });
        //var isSeekingChanges = this.WhenAnyValue(vm => vm.IsSeeking, true)
        //    .SelectMany(value =>
        //        value ? Observable.Return(value) : Observable.Timer(TimeSpan.FromMilliseconds(250)).Select(_ => value)
        //    );
        //var timestamps = Observable
        //    .Interval(TimeSpan.FromSeconds(1))
        //    .Select(_ => DateTimeOffset.Now)
        //    //.Do(_ => Debug.WriteLine("tick"))
        //    .Publish()
        //    .RefCount();
        //var positionObservable = monitor
        //    .PositionChanges
        //    //.Do(_ => Debug.WriteLine("timeline"))
        //    .CombineLatest(isSeekingChanges)
        //    .Where(values => !values.Second)
        //    //.Do(_ => Debug.WriteLine("playback"))
        //    .Select((values) => values.First)
        //    //.Do(x => Debug.WriteLine(x))
        //    .Publish()
        //    .RefCount();

        //_position = positionObservable
        //    //.Do(t => Debug.WriteLine($"Position {t}"))

        //    .ToProperty(this, vm => vm.Position, initialValue: TimeSpan.Zero)
        //    .DisposeWith(_disposable);

        //_progress = positionObservable
        //    .WithLatestFrom(monitor.DurationChanges)
        //    .Select(values => values.Second.Ticks > 0 ? (float)(values.First.Ticks / values.Second.Ticks) : 0)
        //    .ToProperty(this, vm => vm.Progress, initialValue: 0)
        //    .DisposeWith(_disposable);
    }

    public VolumeControlsViewModel DeviceVolumeControls { get; }
    public VolumeControlsViewModel SessionVolumeControls { get; }

    public PlaybackControlsViewModel Playback { get; }
    //public ReactiveCommand<Unit, Unit> PlayPause { get; private set; }
    //public ReactiveCommand<Unit, Unit> Next { get; private set; }
    //public ReactiveCommand<Unit, Unit> Prev { get; private set; }
    //public ReactiveCommand<Unit, Unit> Mute { get; private set; }
    //public ReactiveCommand<Unit, Unit> Pause { get; private set; }
    //public ReactiveCommand<TimeSpan, Unit> Seek { get; }

    //public ReactiveCommand<float, Unit> SetDeviceVolume { get; init; }
    //public ReactiveCommand<float, Unit> SetSessionVolume { get; init; }

    //public ReactiveCommand<bool, Unit> SetSessionMuted { get; init; }
    //public ReactiveCommand<bool, Unit> SetDeviceMuted { get; init; }

    public string ArtistName => _artistName.Value;
    public string AlbumTitle => _albumTitle.Value;
    public string Title => _title.Value;
    public ImageSource? Thumbnail => _thumbnail?.Value;
    //public TimeSpan Duration => _duration.Value;

    //public TimeSpan Position => _position.Value;

    //public float Progress => _progress.Value;

    //public bool IsPlaying => _isPlaying.Value;

    //public bool CanSeek => _canSeek.Value;


    //private bool _isSeeking = false;


    //public bool IsSeeking
    //{
    //    get => _isSeeking;
    //    set => this.RaiseAndSetIfChanged(ref _isSeeking, value);
    //}

    public static async Task<ImageSource?> GetCurrentMediaAlbumArt(IRandomAccessStreamReference? imageStream)
    {
        if (imageStream is not null)
        {
            // The Thumbnail property is a RandomAccessStreamReference
            IRandomAccessStreamWithContentType streamRef = await imageStream.OpenReadAsync();

            // You can now read the stream into a byte array or process it directly
            using (var inputStream = streamRef.AsStreamForRead())
            {
                var imageSource = new BitmapImage { CacheOption = BitmapCacheOption.OnLoad };
                imageSource.BeginInit();
                imageSource.StreamSource = inputStream;
                imageSource.EndInit();
                return imageSource;

                // Example 2: Load into a UI framework's Image source (e.g., WPF, WinForms, UWP)
                // The exact code varies by framework, but you use the 'inputStream'.
                // Example for System.Drawing.Bitmap (WinForms/GDI+):
                // var bitmap = new System.Drawing.Bitmap(inputStream);
            }
        }
        else
        {
            Console.WriteLine("No album art available for the current media.");
        }

        return null;
    }

    //private IObservable<Unit> MuteImpl()
    //{
    //    return Observable.Start(MediaKeySender.Mute);
    //}

    //private IObservable<Unit> PlayPauseImpl()
    //{
    //    return OperationToObservable(_session.MediaSession.TryTogglePlayPauseAsync());
    //}

    //private IObservable<Unit> PrevImpl()
    //{
    //    return OperationToObservable(_session.MediaSession.TrySkipPreviousAsync());
    //}

    //private IObservable<Unit> NextImpl()
    //{
    //    return OperationToObservable(_session.MediaSession.TrySkipNextAsync());
    //}

    //private IObservable<Unit> PauseImpl()
    //{
    //    return OperationToObservable(_session.MediaSession.TryPauseAsync());
    //}

    //private static IObservable<Unit> OperationToObservable(IAsyncOperation<bool> operation)
    //{
    //    return Observable.FromAsync(() => operation.AsTask()).Select(_ => Unit.Default);
    //}

    //private IObservable<Unit> SeekImpl(TimeSpan position)
    //{
    //    if (position > TimeSpan.Zero && position < Duration)
    //    {
    //        return Observable.StartAsync(async () =>
    //        {
    //            var result = await _session.MediaSession.TryChangePlaybackPositionAsync(position.Ticks).AsTask();
    //            Debug.WriteLine($"Seek success: {result}");
    //        });
    //        //return OperationToObservable(_session.TryChangePlaybackPositionAsync(position.Ticks));
    //    }

    //    return Observable.Return(Unit.Default);
    //}

    public void Dispose()
    {
        //Pause.Execute().Subscribe();
        _disposable.Dispose();
    }
}
