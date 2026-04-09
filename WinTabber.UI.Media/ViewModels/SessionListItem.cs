using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows.Media;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.ViewModels;

public class SessionListItem : ReactiveObject, IEquatable<SessionListItem>
{
    public SessionListItem(AggregateSession session)
    {
        Name = session.App.Name;
        _icon = session.App.Icon
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, vm  => vm.Icon, scheduler: RxApp.MainThreadScheduler);
        Aumid = session.MediaSession.SourceAppUserModelId;
        Session = session;

        _icon.ThrownExceptions.Subscribe(ex =>
        {
            Debug.WriteLine("Error getting session app icon {0}", ex);
        });

    }

    public AggregateSession Session { get; init; }
    private readonly ObservableAsPropertyHelper<ImageSource> _icon;
    public string Name { get; init; }
    public ImageSource Icon => _icon.Value;
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
}
