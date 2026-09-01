using System.Reactive.Linq;
using WinTabberUI.Services;
using WinTabberUI.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows the media debug window together with the media controls window, but only while the tray
/// menu toggle is on.
/// </summary>
public class MediaDebugWindowCoordinator : ViewCoordinatorBase<MediaDebugWindow>
{
    private readonly ApplicationStateViewModel _vm;
    private readonly MediaDebugStateService _debugState;

    public MediaDebugWindowCoordinator(
        ApplicationStateViewModel vm,
        MediaDebugStateService debugState,
        IServiceProvider provider
    )
        : base(provider)
    {
        ReuseInstances = true;
        _vm = vm;
        _debugState = debugState;
    }

    protected override IObservable<bool> GetChangeEvents()
    {
        // Both sources replay their current value, so turning the toggle on while the media window
        // is already open opens the debug window at once.
        return _vm
            .IsMediaControlsActiveChanges.CombineLatest(
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
