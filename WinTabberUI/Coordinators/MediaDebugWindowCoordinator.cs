using System.Reactive.Linq;
using WinTabberUI.Services;
using WinTabberUI.Views;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows the media debug window together with the media controls window, but only while the tray
/// menu toggle is on. Depends on MediaWindowViewCoordinator directly (rather than independently
/// re-deriving media-window visibility from the same upstream subject) so this coordinator reacts
/// to the media window's actual shown state — its position in BackgroundServiceContainer's
/// composite no longer has to come after MediaWindowViewCoordinator's for correctness.
/// </summary>
public class MediaDebugWindowCoordinator : ViewCoordinatorBase<MediaDebugWindow>
{
    private readonly MediaWindowViewCoordinator _mediaWindowCoordinator;
    private readonly MediaDebugStateService _debugState;

    public MediaDebugWindowCoordinator(
        MediaWindowViewCoordinator mediaWindowCoordinator,
        MediaDebugStateService debugState,
        IServiceProvider provider
    )
        : base(provider)
    {
        ReuseInstances = true;
        _mediaWindowCoordinator = mediaWindowCoordinator;
        _debugState = debugState;
    }

    protected override IObservable<bool> GetChangeEvents()
    {
        // ShownChanges is a BehaviorSubject (replays its current value), so turning the toggle on
        // while the media window is already open opens the debug window at once.
        return _mediaWindowCoordinator
            .ShownChanges.CombineLatest(
                _debugState.IsEnabledChanges,
                (isMediaVisible, isDebugEnabled) => isMediaVisible && isDebugEnabled
            )
            .DistinctUntilChanged();
    }

    protected override void Show(MediaDebugWindow instance)
    {
        instance.ViewModel.Attach();
        instance.Show();
    }

    protected override void Close(MediaDebugWindow instance)
    {
        instance.Hide();
        instance.ViewModel.Detach();
    }
}
