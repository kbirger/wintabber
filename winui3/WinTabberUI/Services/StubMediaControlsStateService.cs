using System.Reactive.Linq;
using WinTabber.UI.Media.Services;

namespace WinTabberUI.Services;

/// <summary>
/// Placeholder implementation, registered only to satisfy <see cref="WinTabber.ViewModels.ApplicationStateViewModelFactory"/>'s
/// dependency graph so <see cref="WinTabber.ViewModels.WindowSelectorViewModel"/> can be
/// constructed in this phase. The real media-controls state service (WinTabber.UI.Media's
/// MediaControlsStateService, WPF-dependent, not referenced by this project) is ported when
/// MediaControlsWindow itself is — that phase should replace this registration with the real
/// one, not build alongside it.
/// </summary>
public sealed class StubMediaControlsStateService : IMediaControlsStateService
{
    public IObservable<bool> IsMediaControlsVisibleChanges { get; } = Observable.Empty<bool>();

    public void HideView() { }
}
