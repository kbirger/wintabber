using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using WinTabberUI.Models;

namespace WinTabberUI.ViewModels;

public class SessionListItem : ReactiveObject, IEquatable<SessionListItem>
{
    public SessionListItem(AggregateSession session)
    {
        Name = session.App.Name;
        _icon = session.App.Icon.ToProperty(this, vm  => vm.Icon);
        Aumid = session.MediaSession.SourceAppUserModelId;
        Session = session;
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
