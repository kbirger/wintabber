using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.ViewModels;

public class SessionListItem : ReactiveObject, IEquatable<SessionListItem>, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public SessionListItem(AggregateSession session)
    {
        Name = session.App.Name;
        // ObserveOn is still needed so PropertyChanged reaches the UI thread. The decode itself
        // now happens in BitmapToImageSourceConverter on the UI thread during binding, not here,
        // so this no longer needs to run before a Select step that touches the Bitmap off-thread.
        _icon = session.App.Icon
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .ToProperty(this, vm  => vm.Icon, scheduler: RxSchedulers.MainThreadScheduler);
        Aumid = session.MediaSession.SourceAppUserModelId;
        Session = session;

        _disposables.Add(_icon);
        _disposables.Add(_icon.ThrownExceptions.Subscribe(ex =>
        {
            Debug.WriteLine("Error getting session app icon {0}", ex);
        }));

    }

    public AggregateSession Session { get; init; }
    private readonly ObservableAsPropertyHelper<System.Drawing.Bitmap?> _icon;
    public string Name { get; init; }
    public System.Drawing.Bitmap? Icon => _icon.Value;
    public string Aumid { get; init; }

    public bool Equals(SessionListItem? other)
    {
        return string.Equals(other?.Aumid, Aumid, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as SessionListItem);
    }

    public override int GetHashCode()
    {
        return Aumid.GetHashCode();
    }

    // Disposes the icon ObservableAsPropertyHelper's subscription to session.App.Icon.
    // DisposeMany() in MediaControlsViewModel calls this when the item leaves the sessions
    // cache; without it, every SessionListItem ever created stays subscribed to the shared,
    // Replay(1)/AutoConnect() icon observable, which pins both the item and its bitmap forever.
    public void Dispose()
    {
        _disposables.Dispose();
    }
}
