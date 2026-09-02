using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WinTabber.Api.Media.SMTC.Services;

public partial class SMTCSessionMonitor 
{
    /// <summary>
    /// The position that the extrapolation counts from, and the time at which the source app or a
    /// seek reported it.
    /// </summary>
    private readonly record struct TimelineBaseline(TimeSpan Position, DateTimeOffset UpdatedAt, TimeSpan MaxSeekTime);

    private readonly record struct PlaybackTick(DateTimeOffset Now, bool IsPlaying);

    public GlobalSystemMediaTransportControlsSession Session { get; }
    private readonly CompositeDisposable _disposable = new CompositeDisposable();
    private readonly Subject<TimelineBaseline> _seekBaselines = new Subject<TimelineBaseline>();
    public IObservable<string> ArtistNameChanges { get; }
    public IObservable<string> AlbumTitleChanges { get; }
    public IObservable<string> TitleChanges { get; }
    public IObservable<TimeSpan> DurationChanges { get; }
    public IObservable<TimeSpan> PositionChanges { get; }
    public IObservable<float> ProgressChanges { get; }
    public IObservable<bool> IsPlayingChanges { get; }
    public IObservable<bool> CanPlayPauseChanges { get; }
    public IObservable<bool> CanNextChanges { get; }
    public IObservable<bool> CanPrevChanges { get; }
    public IObservable<bool> CanPauseChanges { get; }
    public IObservable<ImageSource?> ThumbnailChanges { get; }
    public IObservable<bool> CanSeekChanges { get; }
    public SMTCSessionMonitor(GlobalSystemMediaTransportControlsSession smtcSession)
    {
        Session = smtcSession;

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


        CanPlayPauseChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsPlayPauseToggleEnabled || info.Controls.IsPauseEnabled || info.Controls.IsPlayEnabled);

        CanNextChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsNextEnabled);

        CanPrevChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsPreviousEnabled);
        CanPauseChanges = playbackPropertiesChanged
            .Select(info => info.Controls.IsPauseEnabled);

        DurationChanges = timelinePropertyChanged.Select(update => update.EndTime);

        // The extrapolation baseline. A source app reports its position rarely, so the position
        // between reports is the last reported position plus the time that passed since. A seek
        // that this application makes also sets the baseline. Without that, the next tick
        // extrapolates from the position before the seek and the slider jumps back.
        var seekBaselines = _seekBaselines.AsObservable();

        var baselines = timelinePropertyChanged
            .Select(timeline => new TimelineBaseline(
                timeline.Position,
                timeline.LastUpdatedTime,
                timeline.MaxSeekTime
            ))
            .Merge(seekBaselines)
            .Replay(1)
            .RefCount();

        // The cached playback status can stay stale after a seek. A read on each tick reports the
        // live status, so the position keeps moving while the source app plays.
        var ticks = Observable
            .Interval(TimeSpan.FromSeconds(1))
            .Select(_ => ReadTick(smtcSession))
            .Where(tick => tick is not null)
            .Select(tick => tick!.Value)
            .Publish()
            .RefCount();

        IsPlayingChanges = playbackPropertiesChanged
            .Select(info => info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            .Merge(ticks.Select(tick => tick.IsPlaying))
            .DistinctUntilChanged();

        var predictedPositions = ticks
            .CombineLatest(baselines)
            .Where(item => item.First.IsPlaying)
            .Select(item =>
            {
                var baseline = item.Second;
                var predictedTime = baseline.Position + (item.First.Now - baseline.UpdatedAt);
                var ticksValue = Math.Min(predictedTime.Ticks, baseline.MaxSeekTime.Ticks);
                return TimeSpan.FromTicks(Math.Max(ticksValue, 0));
            });

        var positionObservable = timelinePropertyChanged
            .Select(timeline => timeline.Position)
            .Merge(seekBaselines.Select(baseline => baseline.Position))
            .Merge(predictedPositions)
            .Publish()
            .RefCount();

        


        PositionChanges = positionObservable;

        ProgressChanges = PositionChanges.CombineLatest(DurationChanges, (position, duration) =>
            duration.TotalSeconds > 0 ? (float)(position.TotalSeconds / duration.TotalSeconds) : 0);
        //ProgressChanges = timelinePropertyChanged
        //    .Select(update => update.EndTime.TotalSeconds > 0 ? (float)(update.Position.TotalSeconds / update.EndTime.TotalSeconds) : 0);

        CanSeekChanges = playbackPropertiesChanged.Select(info => info.Controls.IsPlaybackPositionEnabled);
    }
    /// <summary>
    /// Reads the live playback status. The read replaces the cached status, which can stay stale
    /// after the source app seeks.
    /// </summary>
    private static PlaybackTick? ReadTick(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var info = session.GetPlaybackInfo();
            var isPlaying =
                info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new PlaybackTick(DateTimeOffset.Now, isPlaying);
        }
        catch (Exception ex)
        {
            // A dead session must not complete the position stream. A completed stream freezes the
            // slider until the application restarts.
            Debug.WriteLine($"SMTC playback status read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Moves playback to the given position and sets the extrapolation baseline.
    /// </summary>
    /// <remarks>
    /// The baseline must move with the seek. The source app reports its position rarely, so
    /// without a new baseline the next tick extrapolates from the position before the seek and the
    /// slider jumps back.
    /// </remarks>
    public async Task<bool> SeekAsync(TimeSpan position)
    {
        var success = await Session.TryChangePlaybackPositionAsync(position.Ticks).AsTask();
        if (!success)
        {
            return false;
        }

        var maxSeekTime = TimeSpan.MaxValue;
        try
        {
            maxSeekTime = Session.GetTimelineProperties().MaxSeekTime;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SMTC MaxSeekTime read failed: {ex.GetType().Name}");
        }

        if (maxSeekTime <= TimeSpan.Zero)
        {
            maxSeekTime = TimeSpan.MaxValue;
        }

        _seekBaselines.OnNext(new TimelineBaseline(position, DateTimeOffset.Now, maxSeekTime));
        return true;
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
                imageSource.Freeze();
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
