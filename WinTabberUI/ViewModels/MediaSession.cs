using Microsoft.WindowsAPICodePack.Shell;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Windows.Media;
using Windows.Media.Control;
using WinTabberUI.Infrastructure;

namespace WinTabberUI.ViewModels;

public class MediaSession : ReactiveObject, IComparable<MediaSession>, IEquatable<MediaSession>
{
    public string Id { get; }
    public string Name { get; }
    public string ExePath { get; }

    private readonly ObservableAsPropertyHelper<ImageSource> _icon;
    private static GlobalSystemMediaTransportControlsSession _nativeSession;

    public ImageSource Icon => _icon.Value;

    public MediaSession(string id, string name, IObservable<ImageSource> icon, string exePath)
    {
        Id = id;
        Name = name;
        ExePath = exePath;
        _icon = icon.ToProperty(this, vm => vm.Icon);
    }
    public int CompareTo(MediaSession? other)
    {
        return string.Compare(Id, other?.Id);
    }

    //public static ImageCache _imageCache;
    static MediaSession()
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
    public static MediaSession Create(GlobalSystemMediaTransportControlsSession session, InstalledApplicationInfo app)
    {
        _nativeSession = session;
        var aumid = session.SourceAppUserModelId;

        var newItem = new MediaSession(aumid, app.Name, app.Icon, app.TargetPath);

        return newItem;
    }

    public IObservable<Unit> SendCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Task> command)
    {
        return Observable.StartAsync(() => command.Invoke(_nativeSession), RxApp.MainThreadScheduler);
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as MediaSession);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public bool Equals(MediaSession? other)
    {
        return other?.Id == Id;
    }

    public static bool operator ==(MediaSession left, MediaSession right)
    {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

        return left.Equals(right);
    }

    public static bool operator !=(MediaSession left, MediaSession right)
    {
        return !(left == right);
    }

    public static bool operator <(MediaSession left, MediaSession right)
    {
        return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
    }

    public static bool operator <=(MediaSession left, MediaSession right)
    {
        return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
    }

    public static bool operator >(MediaSession left, MediaSession right)
    {
        return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
    }

    public static bool operator >=(MediaSession left, MediaSession right)
    {
        return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
    }
}
