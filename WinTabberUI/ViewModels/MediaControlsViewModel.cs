using CoreAudio;
using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Serialization;
using Windows.Media.Control;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using WinTabber.Interop;

namespace WinTabberUI.ViewModels;

public class MediaControlsViewModel : ReactiveObject, IActivatableViewModel
{

    private MMDeviceEnumerator _deviceEnum;
    private ObservableAsPropertyHelper<string> _artistName;
    private ObservableAsPropertyHelper<string> _albumTitle;
    private ObservableAsPropertyHelper<string> _title;
    private ObservableAsPropertyHelper<TimeSpan> _duration;
    private ObservableAsPropertyHelper<TimeSpan> _position;
    private ObservableAsPropertyHelper<bool> _isPlaying;
    private ObservableAsPropertyHelper<ImageSource?> _thumbnail;

    public ViewModelActivator Activator { get; } = new ViewModelActivator();
    public ReactiveCommand<Unit, Unit> PlayPause { get; init; }
    public ReactiveCommand<Unit, Unit> Next { get; init; }
    public ReactiveCommand<Unit, Unit> Prev { get; init; }

    public MediaControlsViewModel()
    {
        _deviceEnum = new MMDeviceEnumerator(Guid.NewGuid());
        var scheduler = RxApp.MainThreadScheduler;
        PlayPause = ReactiveCommand.CreateFromObservable(
            PlayPauseImpl,
            canExecute: null,
            outputScheduler: scheduler);
        Next = ReactiveCommand.CreateFromObservable(
            NextImpl,
            canExecute: null,
            outputScheduler: scheduler);
        Prev = ReactiveCommand.CreateFromObservable(
            PrevImpl,
            canExecute: null,
            outputScheduler: scheduler);

        //var playbackWatcher = Observable.Interval(TimeSpan.FromSeconds(1))
        //    .SelectMany(Observable.FromAsync(GetCurrentItemCore))
        //    .Finally(() => Debug.WriteLine("Playback watcher ended"))
        //    .Publish();


        //_artistName = playbackWatcher
        //    .Select(item => item.Media?.Artist ?? string.Empty)
        //    .ToProperty(this, vm => vm.ArtistName);

        //_albumTitle = playbackWatcher
        //    .Select(item => item.Media?.AlbumTitle ?? string.Empty)
        //    .ToProperty(this, vm => vm.AlbumTitle);

        //_title = playbackWatcher
        //    .Select(item => item.Media?.Title ?? string.Empty)
        //    .ToProperty(this, vm => vm.Title);

        //_duration = playbackWatcher
        //    .Select(item => item.Timeline?.EndTime ?? TimeSpan.Zero)
        //    .ToProperty(this, vm => vm.Duration);

        //_position = playbackWatcher
        //    .Select(item => item.Timeline?.Position ?? TimeSpan.Zero)
        //    .ToProperty(this, vm => vm.Position);
        Debug.WriteLine("Created");
        this.WhenActivated((disposables) =>
        {
            Debug.WriteLine("Activated");

            Observable.FromAsync(async () => await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
                .Take(1)
                .Select(manager => manager.GetCurrentSession())
                .Subscribe(session =>
                {
                    session.TimelinePropertiesChanged += (_, _) => { Debug.WriteLine("!====> Timeline changed"); };
                    Debug.WriteLine("Got manager");
                    var mediaPropertyChanges = Observable.FromEventPattern<MediaPropertiesChangedEventArgs>(session, nameof(session.MediaPropertiesChanged))
                        .Select(_ => Unit.Default)
                        .StartWith(Unit.Default)
                        .SelectMany(_ => session.TryGetMediaPropertiesAsync())
                        .Replay(1)
                        .RefCount();
                    var playbackPropertyChanges = Observable.FromEventPattern<PlaybackInfoChangedEventArgs>(session, nameof(session.PlaybackInfoChanged))
                        .Select(_ => Unit.Default)
                        .StartWith(Unit.Default)
                        .Select(_ => session.GetPlaybackInfo())
                        .Replay(1)
                        .RefCount();

                    var timelinePropertyChanges = Observable.FromEventPattern<TimelinePropertiesChangedEventArgs>(session, nameof(session.TimelinePropertiesChanged))
                        .Select(_ => Unit.Default)
                        .StartWith(Unit.Default)
                        .Select(_ =>
                        {
                            Debug.WriteLine("===> Get Timeline"); return session.GetTimelineProperties();
                        })
                        .Replay(1)
                        .RefCount();



                    mediaPropertyChanges = mediaPropertyChanges.Do(_ => Debug.WriteLine("Media properties change"));
                    timelinePropertyChanges = timelinePropertyChanges.Do(_ => Debug.WriteLine("timeline properties change"));
                    playbackPropertyChanges = playbackPropertyChanges.Do(_ => Debug.WriteLine("playback properties change"));

                    mediaPropertyChanges
                        .Select(update => update.Artist)
                        .Do(t => Debug.WriteLine($"artist {t}"))

                        .ToProperty(this, vm => vm.ArtistName, out _artistName, initialValue: "")
                        .DisposeWith(disposables);

                    mediaPropertyChanges
                        .Select(update => update.AlbumTitle)
                        .Do(t => Debug.WriteLine($"album {t}"))

                        .ToProperty(this, vm => vm.AlbumTitle, out _albumTitle, initialValue: "")
                        .DisposeWith(disposables);

                    mediaPropertyChanges
                        .Select(update => update.Title)
                        .Do(t => Debug.WriteLine($"Title {t}"))
                        .ToProperty(this, vm => vm.Title, out _title, initialValue: "")
                        .DisposeWith(disposables);

                    mediaPropertyChanges
                        .ObserveOn(scheduler)
                        .SelectMany(update => GetCurrentMediaAlbumArt(update.Thumbnail))
                        .ToProperty(this, vm => vm.Thumbnail, out _thumbnail, initialValue: new BitmapImage())
                        .DisposeWith(disposables);

                    playbackPropertyChanges
                        .Select(info => info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        .Do(t => Debug.WriteLine($"playing: {t}"))
                        .ToProperty(this, vm => vm.IsPlaying, out _isPlaying)
                        .DisposeWith(disposables);


                    timelinePropertyChanges
                        .Select(update => update.Position)
                        .Do(t => Debug.WriteLine($"Position {t}"))

                        .ToProperty(this, vm => vm.Position, out _position, initialValue: TimeSpan.Zero)
                        .DisposeWith(disposables);

                    timelinePropertyChanges
                        .Select(update => update.EndTime)
                        .Do(t => Debug.WriteLine($"End time {t}"))
                        .ToProperty(this, vm => vm.Position, out _duration, initialValue: TimeSpan.Zero)
                        .DisposeWith(disposables);
                });
        });


        Observable.Merge(
            PlayPause.ThrownExceptions,
            Next.ThrownExceptions,
            Prev.ThrownExceptions
        ).Subscribe(ex =>
        {
            Debug.WriteLine("Error processing media keys");
            Debug.WriteLine(ex);
        });
    }

    public string ArtistName => _artistName?.Value ?? string.Empty;
    public string AlbumTitle => _albumTitle?.Value ?? string.Empty;
    public string Title => _title?.Value ?? string.Empty;

    public TimeSpan Duration => _duration?.Value ?? TimeSpan.Zero;

    public TimeSpan Position => _position?.Value ?? TimeSpan.Zero;

    public ImageSource? Thumbnail => _thumbnail?.Value ?? new BitmapImage();

    public bool IsPlaying => _isPlaying?.Value ?? false;


    private MMDevice? GetDefaultPlaybackDevice()
    {
        try
        {
            return _deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error getting default playback device:");
            Debug.WriteLine(ex);
            return null;
        }
    }
    private MMDeviceCollection GetDevices()
    {
        var devices = _deviceEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        return devices;
    }

    private float GetVolume()
    {
        var device = GetDefaultPlaybackDevice();

        if (device is not null)
        {
            return device.AudioEndpointVolume?.MasterVolumeLevelScalar ?? 0;
        }

        return 0;
    }

    private async Task SetVolume(float volume)
    {
        var device = GetDefaultPlaybackDevice();
        if (device?.AudioEndpointVolume is not null)
        {
            device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
        }

    }




    private async Task<(GlobalSystemMediaTransportControlsSessionMediaProperties? Media, GlobalSystemMediaTransportControlsSessionTimelineProperties? Timeline)> GetCurrentItemCore()
    {
        var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = mgr.GetCurrentSession();
        var item = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var properties = await session.TryGetMediaPropertiesAsync();

        return (properties, timeline);
    }

    private IObservable<Unit> PlayPauseImpl()
    {
        return Observable.Start(MediaKeySender.PlayPause);
    }

    private IObservable<Unit> PrevImpl()
    {
        return Observable.Start(MediaKeySender.Prev);

    }

    private IObservable<Unit> NextImpl()
    {
        return Observable.Start(MediaKeySender.Next);
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
}
