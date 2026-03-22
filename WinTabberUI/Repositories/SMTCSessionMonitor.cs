using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WinTabberUI.Repositories;

public partial class SMTCSessionMonitor : ReactiveObject
{
    private readonly GlobalSystemMediaTransportControlsSession _smtcSession;
    private readonly CompositeDisposable _disposable = new CompositeDisposable();
    public IObservable<string> ArtistNameChanges {get;}
    public IObservable<string> AlbumTitleChanges {get;}
    public IObservable<string> TitleChanges {get;}
    public IObservable<TimeSpan> DurationChanges {get;}
    public IObservable<TimeSpan> PositionChanges {get;}
    public IObservable<float> ProgressChanges {get;}
    public IObservable<bool> IsPlayingChanges {get;}
    public IObservable<bool> CanPlayPauseChanges {get;}
    public IObservable<bool> CanNextChanges {get;}
    public IObservable<bool> CanPrevChanges {get;}
    public IObservable<bool> CanPauseChanges {get;}
    public IObservable<ImageSource?> ThumbnailChanges {get;}
    public IObservable<bool> CanSeekChanges { get; }
    public SMTCSessionMonitor(GlobalSystemMediaTransportControlsSession smtcSession)
    {
        _smtcSession = smtcSession;

        var mediaPropertiesChanged = smtcSession.ObserveMediaProperties();
        var playbackPropertiesChanged = smtcSession.ObservePlaybackProperties();
        var timelinePropertyChanged = smtcSession.ObserveTimelineProperties();

        ArtistNameChanges = mediaPropertiesChanged
            .Select(update => update.Artist);
        //.Do(t => Debug.WriteLine($"artist {t}"))

        AlbumTitleChanges = mediaPropertiesChanged
            .Select(update => update.AlbumTitle);

        TitleChanges = mediaPropertiesChanged
            .Select(update => update.Title);

        ThumbnailChanges = mediaPropertiesChanged
            //.ObserveOn(scheduler)
            .SelectMany(update => GetCurrentMediaAlbumArt(update.Thumbnail));

        IsPlayingChanges = playbackPropertiesChanged
            .Select(info => info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            //.Do(t => Debug.WriteLine($"playing: {t}"))

        
        CanPlayPauseChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsPlayPauseToggleEnabled || info.Controls.IsPauseEnabled || info.Controls.IsPlayEnabled);

        CanNextChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsNextEnabled);

        CanPrevChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsPreviousEnabled);
        CanPauseChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsPauseEnabled);

        DurationChanges = timelinePropertyChanged.Select(update => update.EndTime);

        var timestamps = Observable.Interval(TimeSpan.FromSeconds(1))
          .Select(_ => DateTimeOffset.Now)
          //.Do(_ => Debug.WriteLine("tick"))
          .Publish()
          .RefCount();

        var positionObservable = timelinePropertyChanged
            //.Do(_ => Debug.WriteLine("timeline"))
            .CombineLatest(timestamps, IsPlayingChanges)
            //.Do(_ => Debug.WriteLine("playback"))
            .Select((values) => values.Third ? values.First.Position.Add(values.Second - values.First.LastUpdatedTime) : values.First.Position)
            //.Do(x => Debug.WriteLine(x))
            .Publish()
            .RefCount();

        PositionChanges = positionObservable;

        ProgressChanges = timelinePropertyChanged
            .Select(update => update.EndTime.TotalSeconds > 0 ? (float)(update.Position.TotalSeconds / update.EndTime.TotalSeconds) : 0);

        CanSeekChanges = playbackPropertiesChanged.Select(info => info.Controls.IsPlaybackPositionEnabled);
    }
    public static async Task<ImageSource?> GetCurrentMediaAlbumArt(IRandomAccessStreamReference? imageStream)
    {
        if (imageStream is not null)
        {
            // The Thumbnail property is a RandomAccessStreamReference
            IRandomAccessStreamWithContentType streamRef = await imageStream.OpenReadAsync();

            // You can now read the stream into a byte array or process it directly
            using (Stream inputStream = streamRef.AsStreamForRead())
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
}
