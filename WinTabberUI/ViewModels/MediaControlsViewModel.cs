using CoreAudio;
using DynamicData;
using iNKORE.UI.WPF.Modern.Common;
using iNKORE.UI.WPF.Modern.Controls;
using Microsoft.WindowsAPICodePack.Shell;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Serialization;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Devices.Display.Core;
using Windows.Management.Deployment;
using Windows.Media.Control;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using WinTabber.Interop;
using WinTabberUI.Infrastructure;
using WinTabberUI.Services;

namespace WinTabberUI.ViewModels;

public class MediaControlsViewModel : ReactiveObject, IActivatableViewModel
{
    public class SessionItem : ReactiveObject, IComparable<SessionItem>
    {
        public string Id { get; }
        public string Name { get; }

        private readonly ObservableAsPropertyHelper<ImageSource> _icon;

        public ImageSource Icon => _icon.Value;

        public SessionItem(string id, string name, IObservable<ImageSource> icon)
        {
            Id = id;
            Name = name;
            _icon = icon.ToProperty(this, vm => vm.Icon);
        }
        public int CompareTo(SessionItem? other)
        {
            return string.Compare(Id, other?.Id);
        }

        //public static ImageCache _imageCache;
        static SessionItem()
        {
            //_appCache = Test();
        }
        //private static Dictionary<string, SessionItem> Test()
        //{
        //    var FOLDERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
        //    IKnownFolder appsFolder = KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);
        //    Dictionary<string, SessionItem> lookup = new();
        //    foreach (var app in (IKnownFolder)appsFolder)
        //    {
        //        string name = app.Name;
        //        var props = app.Properties;
        //        var icon = app.Thumbnail.SmallBitmapSource;
        //        // The ParsingName property is the AppUserModelID
        //        string appUserModelID = app.ParsingName; // or app.Properties.System.AppUserModel.ID
        //        //ImageSource icon = app.Thumbnail.MediumBitmapSource;
        //        lookup.Add(appUserModelID, new SessionItem(appUserModelID, name, _imageCache.GetOrAddAsync(appUserModelID, () => app.Thumbnail.SmallBitmapSource)));
        //    }

        //    return lookup;
        //}
        private static readonly Dictionary<string, SessionItem> _appCache = new Dictionary<string, SessionItem>();
        public static SessionItem Create(GlobalSystemMediaTransportControlsSession session, ImageCache imageCache)
        {
            var aumid = session.SourceAppUserModelId;

            //var app = _appCache.GetOrAdd(session.SourceAppUserModelId, static (id) => AppInfo.GetFromAppUserModelId(id));
            if (_appCache.TryGetValue(aumid, out var item))
            {
                return item;
            }
            else
            {
                string displayName = aumid;
                var image = imageCache.LoadingImage;
                if (imageCache.AppFolder2.TryGetValue(aumid, out var appItem))
                {
                    displayName = appItem.Name;
                    image = imageCache.GetOrAddAsync(aumid, () => appItem.Thumbnail.SmallBitmap);
                }

                var newItem = new SessionItem(aumid, displayName, image);
                _appCache[aumid] = newItem;

                return newItem;

            }
        }
    }

    private class GlobalSystemMediaTransportControlsSessionComparer : IComparer<GlobalSystemMediaTransportControlsSession>, IComparer
    {
        public int Compare(GlobalSystemMediaTransportControlsSession? x, GlobalSystemMediaTransportControlsSession? y)
        {
            return x?.SourceAppUserModelId.CompareTo(y?.SourceAppUserModelId) ?? 0;
        }

        int IComparer.Compare(object? x, object? y)
        {
            return this.Compare(x as GlobalSystemMediaTransportControlsSession, y as GlobalSystemMediaTransportControlsSession);
        }
    }

    private MMDeviceEnumerator _deviceEnum;
    private ObservableAsPropertyHelper<string> _artistName;
    private ObservableAsPropertyHelper<string> _albumTitle;
    private ObservableAsPropertyHelper<string> _title;
    private ObservableAsPropertyHelper<TimeSpan> _duration;
    private ObservableAsPropertyHelper<TimeSpan> _position;
    private ObservableAsPropertyHelper<bool> _isPlaying;
    private ObservableAsPropertyHelper<ImageSource?> _thumbnail;
    private IReadOnlyList<GlobalSystemMediaTransportControlsSession> _sessionModels;
    private IReadOnlyList<SessionItem> _sessions;
    private SessionItem _activeSession;
    private readonly ImageCache _imageCache;
    private readonly IMediaControlsStateService _mediaControlsStateService;

    public ViewModelActivator Activator { get; } = new ViewModelActivator();
    public ReactiveCommand<Unit, Unit> PlayPause { get; init; }
    public ReactiveCommand<Unit, Unit> Next { get; init; }
    public ReactiveCommand<Unit, Unit> Prev { get; init; }

    //public static async Task<IReadOnlyList<AppListEntry>> GetAppListEntries()
    //{
    //    var packageManager = new PackageManager();
    //    // Iterate through all installed packages for the current user

    //    List<AppListEntry> list = new();
    //    foreach (var package in packages)
    //    {
    //        var appListEntries = await package.GetAppListEntriesAsync();
    //        list.AddRange(appListEntries);
    //        //foreach (var entry in appListEntries)
    //        //{
    //        //    // Check if the entry's AUMID matches the target
    //        //    if (string.Equals(entry.AppUserModelId, targetAumid, StringComparison.OrdinalIgnoreCase))
    //        //    {
    //        //        // Return the display name
    //        //        return entry.DisplayInfo.DisplayName;
    //        //    }
    //        //}
    //    }

    //    return list;
    //}



    public MediaControlsViewModel(ImageCache imageCache, IMediaControlsStateService mediaControlsStateService)
    {
        _imageCache = imageCache;
        _mediaControlsStateService = mediaControlsStateService;
        Sessions = [];
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

            Disposable.Create(HandleDeactivation)
                .DisposeWith(disposables);


            var observableManager = Observable.FromAsync(async () => await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
                .Replay(1)
                .RefCount();

            var sessionsListUpdates = observableManager
                .SelectMany(manager => Observable.FromEventPattern<SessionsChangedEventArgs>(manager, nameof(manager.SessionsChanged))
                .Select(_ => Unit.Default)
                .StartWith(Unit.Default)
                .Select(_ => manager.GetSessions())
                .Do(sessions => { Debug.WriteLine(sessions.Select(session => session.SourceAppUserModelId).ToArray()); })
                .Replay(1)
                .RefCount());

            // bind sessions list
            sessionsListUpdates
                .ObserveOn(RxApp.TaskpoolScheduler)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(sessions =>
                {
                    Sessions = sessions.Select(x => SessionItem.Create(x, _imageCache)).ToArray();
                })
                .DisposeWith(disposables);


            observableManager
                .Select(manager =>
                {
                    // Handle WinRT session change
                    var currentSessionChanged = Observable.FromEventPattern<CurrentSessionChangedEventArgs>(manager, nameof(manager.CurrentSessionChanged))
                        .Select(_ => Unit.Default)
                        .StartWith(Unit.Default)
                        .Select(_ => manager.GetCurrentSession())
                        .Do(x =>
                        {
                            Debug.WriteLine($"WINRT: Current session changed. Got new session {x?.SourceAppUserModelId}");
                        })
                        .Replay(1)
                        .RefCount();
                    return currentSessionChanged;
                })
                .Switch()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(session =>
                {
                    if (session is null)
                    {
                        return;
                    }
                    Debug.WriteLine($"WinRT new session: {session.SourceAppUserModelId}");
                    foreach (var s in Sessions)
                    {
                        Debug.WriteLine($"sessoin: {s.Id}: {s.Name}");

                    }
                    ActiveSession = SessionItem.Create(session, _imageCache); //Sessions.FirstOrDefault(x => x.Id == session.SourceAppUserModelId)!;
                    var mediaPropertyChanges = Observable.FromEventPattern<MediaPropertiesChangedEventArgs>(session, nameof(session.MediaPropertiesChanged))
                        .Select(_ => Unit.Default)
                        .StartWith(Unit.Default)
                        .SelectMany(_ => session.TryGetMediaPropertiesAsync())
                        .Do(p => { Debug.WriteLine($"MEDIA PROPERTIES CHANGED {p.AlbumArtist} {p.AlbumTitle} {p.Artist} {p.Title}"); })
                        .ObserveOn(scheduler)
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
                            //Debug.WriteLine("===> Get Timeline"); 
                            return session.GetTimelineProperties();
                        })
                        .Replay(1)
                        .RefCount();



                    //mediaPropertyChanges = mediaPropertyChanges.Do(_ => Debug.WriteLine("Media properties change"));
                    //timelinePropertyChanges = timelinePropertyChanges.Do(_ => Debug.WriteLine("timeline properties change"));
                    //playbackPropertyChanges = playbackPropertyChanges.Do(_ => Debug.WriteLine("playback properties change"));

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
                        .ToProperty(this, vm => vm.Thumbnail, out _thumbnail, initialValue: null)
                        .DisposeWith(disposables);

                    playbackPropertyChanges
                        .Select(info => info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        //.Do(t => Debug.WriteLine($"playing: {t}"))
                        .ToProperty(this, vm => vm.IsPlaying, out _isPlaying)
                        .DisposeWith(disposables);


                    timelinePropertyChanges
                        .Select(update => update.Position)
                        //.Do(t => Debug.WriteLine($"Position {t}"))

                        .ToProperty(this, vm => vm.Position, out _position, initialValue: TimeSpan.Zero)
                        .DisposeWith(disposables);

                    timelinePropertyChanges
                        .Select(update => update.EndTime)
                        //.Do(t => Debug.WriteLine($"End time {t}"))
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

    private void HandleDeactivation()
    {
        _mediaControlsStateService.HideView();
    }

    public string ArtistName => _artistName?.Value ?? string.Empty;
    public string AlbumTitle => _albumTitle?.Value ?? string.Empty;
    public string Title => _title?.Value ?? string.Empty;
    public IReadOnlyList<SessionItem> Sessions
    {
        get => _sessions;
        set => this.RaiseAndSetIfChanged(ref _sessions, value);
    }
    public TimeSpan Duration => _duration?.Value ?? TimeSpan.Zero;

    public TimeSpan Position => _position?.Value ?? TimeSpan.Zero;

    public ImageSource? Thumbnail => _thumbnail?.Value;

    public bool IsPlaying => _isPlaying?.Value ?? false;

    public SessionItem ActiveSession
    {
        get => _activeSession;
        set => this.RaiseAndSetIfChanged(ref _activeSession, value);
    }


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
