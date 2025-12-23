using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinTabber.Interop;
using WinTabberUI.Infrastructure;

namespace WinTabberUI.ViewModels;

public class MediaSessionViewModel : ReactiveObject, IDisposable
{
    private readonly ImageCache _imageCache;
    private ObservableAsPropertyHelper<string> _artistName;
    private ObservableAsPropertyHelper<string> _albumTitle;
    private ObservableAsPropertyHelper<string> _title;
    private ObservableAsPropertyHelper<TimeSpan> _duration;
    private ObservableAsPropertyHelper<TimeSpan> _position;
    private ObservableAsPropertyHelper<float> _progress;
    private ObservableAsPropertyHelper<bool> _isPlaying;
    private ObservableAsPropertyHelper<bool> _isMuted;
    private ObservableAsPropertyHelper<ImageSource?> _thumbnail;
    private ObservableAsPropertyHelper<bool> _canSeek;
    private GlobalSystemMediaTransportControlsSession _session;
    private readonly CompositeDisposable _disposable = new CompositeDisposable();
    public MediaSessionViewModel(GlobalSystemMediaTransportControlsSession session, ImageCache imageCache)
    {
        _session = session;
        var scheduler = RxApp.MainThreadScheduler;
        _imageCache = imageCache;

        var mediaPropertiesChanged = ObserveMediaProperties(session);
        var playbackPropertiesChanged = ObservePlaybackProperties(session);
        var timelinePropertyChanged = ObserveTimelineProperties(session);

        mediaPropertiesChanged
            .Select(update => update.Artist)
            //.Do(t => Debug.WriteLine($"artist {t}"))
            .ToProperty(this, vm => vm.ArtistName, out _artistName, initialValue: "")
            .DisposeWith(_disposable);

        mediaPropertiesChanged
            .Select(update => update.AlbumTitle)
            .ToProperty(this, vm => vm.AlbumTitle, out _albumTitle, initialValue: "")
            .DisposeWith(_disposable);

        mediaPropertiesChanged
            .Select(update => update.Title)
            //.Do(t => Debug.WriteLine($"Title {t}"))
            .ToProperty(this, vm => vm.Title, out _title, initialValue: "")
            .DisposeWith(_disposable);

        mediaPropertiesChanged
            .ObserveOn(scheduler)
            .SelectMany(update => GetCurrentMediaAlbumArt(update.Thumbnail))
            .ToProperty(this, vm => vm.Thumbnail, out _thumbnail, initialValue: null)
            .DisposeWith(_disposable);

        playbackPropertiesChanged
            .Select(info => info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            //.Do(t => Debug.WriteLine($"playing: {t}"))
            .ToProperty(this, vm => vm.IsPlaying, out _isPlaying)
            .DisposeWith(_disposable);

        var canPlayPause = playbackPropertiesChanged
            .Select(info => info.Controls.IsPlayPauseToggleEnabled || info.Controls.IsPauseEnabled || info.Controls.IsPlayEnabled);

        var canNext = playbackPropertiesChanged
            .Select(info => info.Controls.IsNextEnabled);

        var canPrev = playbackPropertiesChanged
            .Select(info => info.Controls.IsPreviousEnabled);
        var canPause = playbackPropertiesChanged
            .Select(info =>  info.Controls.IsPauseEnabled);


        var canSeek = playbackPropertiesChanged
            .Select(info => info.Controls.IsPlaybackPositionEnabled);

        PlayPause = ReactiveCommand.CreateFromObservable(
            PlayPauseImpl,
            canExecute: canPlayPause,
            outputScheduler: scheduler).DisposeWith(_disposable);
        Next = ReactiveCommand.CreateFromObservable(
            NextImpl,
            canExecute: canNext,
            outputScheduler: scheduler).DisposeWith(_disposable);
        Prev = ReactiveCommand.CreateFromObservable(
            PrevImpl,
            canExecute: canPrev,
            outputScheduler: scheduler).DisposeWith(_disposable);

        Mute = ReactiveCommand.CreateFromObservable(
            MuteImpl,
            canExecute: null,
            outputScheduler: scheduler).DisposeWith(_disposable);

        Pause = ReactiveCommand.CreateFromObservable(
            PauseImpl,
            canExecute: canPause,
            outputScheduler: scheduler).DisposeWith(_disposable);

        Seek = ReactiveCommand.CreateFromObservable<TimeSpan, Unit>(
            SeekImpl,
            canExecute: canSeek,
            outputScheduler: scheduler).DisposeWith(_disposable);

        canSeek.ToProperty(this, vm => vm.CanSeek, out _canSeek)
            .DisposeWith(_disposable);

        Observable.Merge(
            PlayPause.ThrownExceptions,
            Next.ThrownExceptions,
            Prev.ThrownExceptions
        ).Subscribe(ex =>
        {
            Debug.WriteLine("Error processing media keys");
            Debug.WriteLine(ex);
        });
        _isMuted = Observable.Return(false).ToProperty(this, vm => vm.IsMuted);
        var isSeekingChanges = this.WhenAnyValue(vm => vm.IsSeeking, true)
            .SelectMany(value => value ? Observable.Return(value) : Observable.Timer(TimeSpan.FromMilliseconds(250)).Select(_ => value));
        var timestamps = Observable.Interval(TimeSpan.FromSeconds(1))
            .Select(_ => DateTimeOffset.Now)
            //.Do(_ => Debug.WriteLine("tick"))
            .Publish()
            .RefCount();
        var positionObservable = timelinePropertyChanged
            //.Do(_ => Debug.WriteLine("timeline"))
            .CombineLatest(timestamps, playbackPropertiesChanged, isSeekingChanges)
            .Where(values => !values.Fourth)
            //.Do(_ => Debug.WriteLine("playback"))
            .Select((values) => values.Third.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? values.First.Position.Add(values.Second - values.First.LastUpdatedTime) : values.First.Position)
            //.Do(x => Debug.WriteLine(x))
            .Publish()
            .RefCount();

        positionObservable
            //.Do(t => Debug.WriteLine($"Position {t}"))

            .ToProperty(this, vm => vm.Position, out _position, initialValue: TimeSpan.Zero)
            .DisposeWith(_disposable);

        positionObservable
            .WithLatestFrom(timelinePropertyChanged)
            .Select(values => values.Second.EndTime.Ticks > 0 ? (float)(values.First.Ticks / values.Second.EndTime.Ticks) : 0)
            .ToProperty(this, vm => vm.Progress, out _progress, initialValue: 0)
            .DisposeWith(_disposable);

        timelinePropertyChanged
            .Select(update => update.EndTime)
            //.Do(t => Debug.WriteLine($"End time {t}"))
            .ToProperty(this, vm => vm.Duration, out _duration, initialValue: TimeSpan.Zero)
            .DisposeWith(_disposable);
    }

    public ReactiveCommand<Unit, Unit> PlayPause { get; private set; }
    public ReactiveCommand<Unit, Unit> Next { get; private set; }
    public ReactiveCommand<Unit, Unit> Prev { get; private set; }
    public ReactiveCommand<Unit, Unit> Mute { get; private set; }
    public ReactiveCommand<Unit, Unit> Pause { get; private set; }
    public ReactiveCommand<TimeSpan, Unit> Seek { get; }

    public string ArtistName => _artistName?.Value ?? string.Empty;
    public string AlbumTitle => _albumTitle?.Value ?? string.Empty;
    public string Title => _title?.Value ?? string.Empty;
    public TimeSpan Duration => _duration?.Value ?? TimeSpan.Zero;

    public TimeSpan Position => _position?.Value ?? TimeSpan.Zero;

    public float Progress => _progress?.Value ?? 0;
    public ImageSource? Thumbnail => _thumbnail?.Value;

    public bool IsMuted => _isMuted?.Value ?? false;
    public bool IsPlaying => _isPlaying?.Value ?? false;

    public bool CanSeek => _canSeek?.Value ?? false;

    private bool _isSeeking = false;

    public bool IsSeeking
    {
        get => _isSeeking;
        set => this.RaiseAndSetIfChanged(ref _isSeeking, value);
    }


    private static IObservable<GlobalSystemMediaTransportControlsSessionTimelineProperties> ObserveTimelineProperties(GlobalSystemMediaTransportControlsSession session)
    {
        return EventHelper.EventOrEmpty<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs, GlobalSystemMediaTransportControlsSessionTimelineProperties>(
                session,
                h => session.TimelinePropertiesChanged += h,
                h => session.TimelinePropertiesChanged -= h,
                events => events
                    .Select(_ => Unit.Default)
                    .StartWith(Unit.Default)
                    .Select(_ => session.GetTimelineProperties())

            )
            .Do(_ => Debug.WriteLine("timeline updated"))
            .Replay(1)
            .RefCount();
    }

    private static IObservable<GlobalSystemMediaTransportControlsSessionPlaybackInfo> ObservePlaybackProperties(GlobalSystemMediaTransportControlsSession session)
    {
        return EventHelper.EventOrEmpty<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs, GlobalSystemMediaTransportControlsSessionPlaybackInfo>(
                session,
                h => session.PlaybackInfoChanged += h,
                h => session.PlaybackInfoChanged -= h,
                events => events
                    .Select(_ => Unit.Default)
                    .StartWith(Unit.Default)
                    .Select(_ => session.GetPlaybackInfo())
            )
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();
    }

    private static IObservable<GlobalSystemMediaTransportControlsSessionMediaProperties> ObserveMediaProperties(GlobalSystemMediaTransportControlsSession session)
    {
        return EventHelper.EventOrEmpty<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs, GlobalSystemMediaTransportControlsSessionMediaProperties>(
                session,
                h => session.MediaPropertiesChanged += h,
                h => session.MediaPropertiesChanged -= h,
                events => events
                    .Do(p => { Debug.WriteLine($"MEDIA PROPERTIES CHANGED"); })
                    .SelectMany(_ => session.TryGetMediaPropertiesAsync())
            )
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();
    }

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

    private IObservable<Unit> MuteImpl()
    {
        return Observable.Start(MediaKeySender.Mute);
    }

    private IObservable<Unit> PlayPauseImpl()
    {
        return OperationToObservable(_session.TryTogglePlayPauseAsync());
    }

    private IObservable<Unit> PrevImpl()
    {
        return OperationToObservable(_session.TrySkipPreviousAsync());
    }

    private IObservable<Unit> NextImpl()
    {
        return OperationToObservable(_session.TrySkipNextAsync());
    }

    private IObservable<Unit> PauseImpl()
    {
        return OperationToObservable(_session.TryPauseAsync());
    }

    private static IObservable<Unit> OperationToObservable(IAsyncOperation<bool> operation)
    {
        return Observable.FromAsync(() => operation.AsTask()).Select(_ => Unit.Default);
    }

    private IObservable<Unit> SeekImpl(TimeSpan position)
    {
        if(position > TimeSpan.Zero && position < Duration)
        {
            return OperationToObservable(_session.TryChangePlaybackPositionAsync(position.Ticks));
        }

        return Observable.Return(Unit.Default);
    }

    public void Dispose()
    {
        Pause.Execute().Subscribe();
        _disposable.Dispose();
    }
}
