using ReactiveUI;
using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;
using WinTabber.Api.Media.SMTC.Services;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.ViewModels;

public class MediaSessionViewModel : ReactiveObject, IDisposable
{
    private readonly ObservableAsPropertyHelper<string> _artistName;
    private readonly ObservableAsPropertyHelper<string> _albumTitle;
    private readonly ObservableAsPropertyHelper<string> _title;
    private readonly ObservableAsPropertyHelper<ImageSource?> _thumbnail;


    private readonly AggregateSession _session;
    private readonly AudioSessionService _sessionService;
    private readonly AudioDeviceService _deviceService;
    private readonly CompositeDisposable _disposable = new CompositeDisposable();


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
        
    }

    public VolumeControlsViewModel DeviceVolumeControls { get; }
    public VolumeControlsViewModel SessionVolumeControls { get; }

    public PlaybackControlsViewModel Playback { get; }

    public string ArtistName => _artistName.Value;
    public string AlbumTitle => _albumTitle.Value;
    public string Title => _title.Value;
    public ImageSource? Thumbnail => _thumbnail?.Value;


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

 

    public void Dispose()
    {
        //Pause.Execute().Subscribe();
        _disposable.Dispose();
    }
}
