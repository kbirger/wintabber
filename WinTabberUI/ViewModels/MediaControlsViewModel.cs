using CommunityToolkit.Mvvm.ComponentModel;
using CoreAudio;
using DynamicData;
using iNKORE.UI.WPF.Modern.Common;
using iNKORE.UI.WPF.Modern.Controls;
using Microsoft.WindowsAPICodePack.Shell;
using MS.WindowsAPICodePack.Internal;
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
    //public class DeviceItem : ReactiveObject
    //{
    //    public DeviceItem(MMDevice device)
    //    {
    //        Name = device.DeviceInterfaceFriendlyName;
    //        Id = device.ID;
    //        _isSelected = device.Selected;
    //        _device = device;
    //    }
    //    public string Name { get; }
    
    //    public string Id { get; }

    //    private readonly ObservableAsPropertyHelper<bool> _isActive;
    //    private readonly MMDevice _device;
    //    private bool _isSelected;
    //    public bool IsActive
    //    {
    //        get => _isActive.Value;
    //    }

    //    //public bool IsSelected
    //    //{
    //    //    get => _isSelected;
    //    //    set
    //    //    {
    //    //        _device.Selected = value;
    //    //        this.RaiseAndSetIfChanged(ref _isSelected, value);
    //    //    }
    //    //}
    //}
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

    //private class GlobalSystemMediaTransportControlsSessionComparer : IComparer<GlobalSystemMediaTransportControlsSession>, IComparer
    //{
    //    public int Compare(GlobalSystemMediaTransportControlsSession? x, GlobalSystemMediaTransportControlsSession? y)
    //    {
    //        return x?.SourceAppUserModelId.CompareTo(y?.SourceAppUserModelId) ?? 0;
    //    }

    //    int IComparer.Compare(object? x, object? y)
    //    {
    //        return this.Compare(x as GlobalSystemMediaTransportControlsSession, y as GlobalSystemMediaTransportControlsSession);
    //    }
    //}

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
    //private ObservableAsPropertyHelper<DeviceItem[]> _playbackDevices;
    //private ObservableAsPropertyHelper<DeviceItem[]> _recordingDevices;

    private AudioDeviceSelectorViewModel? _playback;
    private AudioDeviceSelectorViewModel? _recording;

    public AudioDeviceSelectorViewModel? Playback
    {
        get => _playback;
        set => this.RaiseAndSetIfChanged(ref _playback, value);
    }
    public AudioDeviceSelectorViewModel? Recording
    {
        get => _recording;
        set => this.RaiseAndSetIfChanged(ref _recording, value);
    }

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


    private static IObservable<TResult> EventOrEmpty<TSource, TEventArgs, TResult>(
        TSource? source,
        Action<Windows.Foundation.TypedEventHandler<TSource, TEventArgs>> addHandler,
        Action<Windows.Foundation.TypedEventHandler<TSource, TEventArgs>> removeHandler,
        Func<IObservable<Unit>, IObservable<TResult>> eventObservable)
    {
        if (source is null)
        {
            return Observable.Empty<TResult>();
        }


        var obs = Observable.FromEvent<Windows.Foundation.TypedEventHandler<TSource, TEventArgs>, TEventArgs>(
            handler =>
            {
                Windows.Foundation.TypedEventHandler<TSource, TEventArgs> typedHandler = (sender, e) => { handler(e); };

                return typedHandler;
            },
            addHandler,
            removeHandler)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);
        return eventObservable(obs);
    }

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

        
        Debug.WriteLine("Created");
        this.WhenActivated((disposables) =>
        {
            Debug.WriteLine("Activated");

            Disposable.Create(HandleDeactivation)
                .DisposeWith(disposables);

            //var playbackDevices = 
            //    GetDevices(DataFlow.Render)
            //        .Select(x => new DeviceItem(x)).ToArray();
            //var recordingDevices = 
            //    GetDevices(DataFlow.Capture)
            //        .Select(x => new DeviceItem(x)).ToArray();

            //Observable.Return(playbackDevices).ToProperty(this, vm => vm.PlaybackDevices, out _playbackDevices ).ThrownExceptions.Subscribe(ex => { Debug.WriteLine(ex); }) ;
            //Observable.Return(recordingDevices).ToProperty(this, vm => vm.RecordingDevices, out _recordingDevices).ThrownExceptions.Subscribe(ex => { Debug.WriteLine(ex); });

            Playback = new AudioDeviceSelectorViewModel(DataFlow.Render);
            Recording = new AudioDeviceSelectorViewModel(DataFlow.Capture);

            var observableManager = Observable.FromAsync(async () => await GlobalSystemMediaTransportControlsSessionManager.RequestAsync())
                .Replay(1)
                .RefCount();
            IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> sessionsListUpdates = ObserveSessionsList(observableManager);
            // bind sessions list

            BindSessionsList(sessionsListUpdates)
                .DisposeWith(disposables);

            var currentSessionChanged = ObserveCurrentSession(observableManager);
            var mediaPropertiesChanged = ObserveMediaProperties(currentSessionChanged);
            var playbackPropertiesChanged = ObservePlaybackProperties(currentSessionChanged);
            var timelinePropertyChanged = ObserveTimelineProperties(currentSessionChanged);

            mediaPropertiesChanged
                .Select(update => update.Artist)
                .Do(t => Debug.WriteLine($"artist {t}"))
                .ToProperty(this, vm => vm.ArtistName, out _artistName, initialValue: "")
                .DisposeWith(disposables);

            mediaPropertiesChanged
                .Select(update => update.AlbumTitle)
                .ToProperty(this, vm => vm.AlbumTitle, out _albumTitle, initialValue: "")
                .DisposeWith(disposables);

            mediaPropertiesChanged
                .Select(update => update.Title)
                .Do(t => Debug.WriteLine($"Title {t}"))
                .ToProperty(this, vm => vm.Title, out _title, initialValue: "")
                .DisposeWith(disposables);

            mediaPropertiesChanged
                .ObserveOn(scheduler)
                .SelectMany(update => GetCurrentMediaAlbumArt(update.Thumbnail))
                .ToProperty(this, vm => vm.Thumbnail, out _thumbnail, initialValue: null)
                .DisposeWith(disposables);

            playbackPropertiesChanged
                .Select(info => info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                //.Do(t => Debug.WriteLine($"playing: {t}"))
                .ToProperty(this, vm => vm.IsPlaying, out _isPlaying)
                .DisposeWith(disposables);


            timelinePropertyChanged
                .Select(update => update.Position)
                //.Do(t => Debug.WriteLine($"Position {t}"))

                .ToProperty(this, vm => vm.Position, out _position, initialValue: TimeSpan.Zero)
                .DisposeWith(disposables);

            timelinePropertyChanged
                .Select(update => update.EndTime)
                //.Do(t => Debug.WriteLine($"End time {t}"))
                .ToProperty(this, vm => vm.Position, out _duration, initialValue: TimeSpan.Zero)
                .DisposeWith(disposables);


            currentSessionChanged.Subscribe(session =>
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
                                                                          //var mediaPropertyChanges = Observable.FromEventPattern<MediaPropertiesChangedEventArgs>(session, nameof(session.MediaPropertiesChanged))

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

    private static IObservable<GlobalSystemMediaTransportControlsSessionTimelineProperties> ObserveTimelineProperties(IObservable<GlobalSystemMediaTransportControlsSession> currentSessionChanged)
    {
        return currentSessionChanged
            .Select(session => EventOrEmpty<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs, GlobalSystemMediaTransportControlsSessionTimelineProperties>(
                session,
                h => session.TimelinePropertiesChanged += h,
                h => session.TimelinePropertiesChanged -= h,
                events => events
                    .Select(_ => Unit.Default)
                    .StartWith(Unit.Default)
                    .Select(_ => session.GetTimelineProperties())

            ))
            .Switch()
            .Replay(1)
            .RefCount();
    }

    private static IObservable<GlobalSystemMediaTransportControlsSessionPlaybackInfo> ObservePlaybackProperties(IObservable<GlobalSystemMediaTransportControlsSession> currentSessionChanged)
    {
        return currentSessionChanged
            .Select(session => EventOrEmpty<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs, GlobalSystemMediaTransportControlsSessionPlaybackInfo>(
                session,
                h => session.PlaybackInfoChanged += h,
                h => session.PlaybackInfoChanged -= h,
                events => events
                    .Select(_ => Unit.Default)
                    .StartWith(Unit.Default)
                    .Select(_ => session.GetPlaybackInfo())
            ))
            .Switch()
            .Replay(1)
            .RefCount();
    }

    private static IObservable<GlobalSystemMediaTransportControlsSessionMediaProperties> ObserveMediaProperties(IObservable<GlobalSystemMediaTransportControlsSession> currentSessionChanged)
    {
        return currentSessionChanged
            .Select(session => EventOrEmpty<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs, GlobalSystemMediaTransportControlsSessionMediaProperties>(
                session,
                h => session.MediaPropertiesChanged += h,
                h => session.MediaPropertiesChanged -= h,
                events => events
                    .Do(p => { Debug.WriteLine($"MEDIA PROPERTIES CHANGED"); })
                    .SelectMany(_ => session.TryGetMediaPropertiesAsync())
            ))
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();
    }

    private static IObservable<GlobalSystemMediaTransportControlsSession> ObserveCurrentSession(IObservable<GlobalSystemMediaTransportControlsSessionManager> observableManager)
    {


        // Handle WinRT session change
        return observableManager
            .Select(manager =>
                EventOrEmpty<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs, GlobalSystemMediaTransportControlsSession>(
                    manager,
                    h => manager.CurrentSessionChanged += h,
                    h => manager.CurrentSessionChanged -= h,
                    events => events.Select(_ => manager.GetCurrentSession())
                    ))
                .Switch()
                .Do(x =>
                {
                    Debug.WriteLine($"Session changed event");
                })
                .DistinctUntilChanged(x => x?.SourceAppUserModelId)
                .Do(x =>
                {
                    Debug.WriteLine($"WINRT: Current session changed. Got new session {x?.SourceAppUserModelId ?? "no session"}");
                })
                .Replay(1)
                .RefCount()
                .ObserveOn(RxApp.MainThreadScheduler);
    }

    private IDisposable BindSessionsList(IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> sessionsListUpdates)
    {
        return sessionsListUpdates
                        .ObserveOn(RxApp.TaskpoolScheduler)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(sessions =>
                        {
                            Sessions = sessions.Select(x => SessionItem.Create(x, _imageCache)).ToArray();
                        });
    }

    private static IObservable<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> ObserveSessionsList(IObservable<GlobalSystemMediaTransportControlsSessionManager> observableManager)
    {
        return observableManager
            .Select(manager => Observable.FromEventPattern<SessionsChangedEventArgs>(manager, nameof(manager.SessionsChanged))
                .Select(_ => Unit.Default)
                .StartWith(Unit.Default)
                .Select(_ => manager.GetSessions())
                .Do(sessions => { Debug.WriteLine(string.Join(", ", sessions.Select(session => session.SourceAppUserModelId).ToArray())); })
            )
            .Replay(1)
            .RefCount()
            .Switch();
    }

    private void HandleDeactivation()
    {
        _mediaControlsStateService.HideView();
    }

    //public AudioDeviceSelectorViewModel.DeviceItem[] PlaybackDevices => _playbackDevices?.Value ?? [];
    //public DeviceItem[] RecordingDevices => _recordingDevices?.Value ?? [];
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
    private MMDeviceCollection GetDevices(DataFlow dataFlow)
    {
        var devices = _deviceEnum.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
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
