using Microsoft.WindowsAPICodePack.Shell;
using ReactiveUI;
using System.Windows.Media;
using Windows.Media.Control;
using WinTabberUI.Infrastructure;

namespace WinTabberUI.ViewModels;

public class SessionItem : ReactiveObject, IComparable<SessionItem>, IEquatable<SessionItem>
{
    public string Id { get; }
    public string Name { get; }
    public string ExePath { get; }

    private readonly ObservableAsPropertyHelper<ImageSource> _icon;

    public ImageSource Icon => _icon.Value;

    public SessionItem(string id, string name, IObservable<ImageSource> icon, string exePath)
    {
        Id = id;
        Name = name;
        ExePath = exePath;
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
    public static SessionItem Create(GlobalSystemMediaTransportControlsSession session, AppCache imageCache)
    {
        var aumid = session.SourceAppUserModelId;

        //var app = _appCache.GetOrAdd(session.SourceAppUserModelId, static (id) => AppInfo.GetFromAppUserModelId(id));
        var app = imageCache.GetByAumid(aumid);
        var newItem = new SessionItem(aumid, app.Name, app.Icon, app.ExecutablePath);

        return newItem;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as SessionItem);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public bool Equals(SessionItem? other)
    {
        return other?.Id == Id;
    }

    public static bool operator ==(SessionItem left, SessionItem right)
    {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

        return left.Equals(right);
    }

    public static bool operator !=(SessionItem left, SessionItem right)
    {
        return !(left == right);
    }

    public static bool operator <(SessionItem left, SessionItem right)
    {
        return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
    }

    public static bool operator <=(SessionItem left, SessionItem right)
    {
        return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
    }

    public static bool operator >(SessionItem left, SessionItem right)
    {
        return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
    }

    public static bool operator >=(SessionItem left, SessionItem right)
    {
        return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
    }
}
