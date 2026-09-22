using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Api.Windowing.Suspension;
using WinTabber.Events;
using WinTabber.ViewModels;
using WinTabberUI.Models.Settings;
using WinTabberUI.Views;
using WinUIEx;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows or hides the singleton <see cref="SuspendedWindowsWindow"/>. Ported from the WPF app's own
/// <c>SuspendedWindowsViewCoordinator</c> (named <c>...WindowCoordinator</c> here, not
/// <c>...ViewCoordinator</c>, matching every other coordinator already ported in this app), which
/// extends <c>ViewCoordinatorBase&lt;T&gt;</c> -- not ported to this app (see
/// <see cref="WindowSelectorWindowCoordinator"/>'s own doc comment for why). Follows
/// <see cref="WindowSelectorWindowCoordinator"/>'s shape instead: singleton, lazily constructed once,
/// then just <c>Show()</c>/<c>Hide()</c>, matching the WPF original's own <c>ReuseInstances = true</c>.
/// </summary>
/// <remarks>
/// Visible while the switcher is open and something is actually suspended, or pinned open
/// independently via the "sleeping windows" hotkey (<see cref="EventType.CmdSuspendedWindows"/>),
/// same logic as the WPF original -- see <see cref="GetVisibilityChanges"/>.
/// </remarks>
public class SuspendedWindowsWindowCoordinator : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDisposable _subscription;
    private SuspendedWindowsWindow? _window;

    public SuspendedWindowsWindowCoordinator(
        WindowSelectorViewModel selectorViewModel,
        IProcessSuspensionService suspensionService,
        WinTabberEventManager eventManager,
        ApplicationSettings settings,
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        _subscription = GetVisibilityChanges(selectorViewModel, suspensionService, eventManager, settings.General)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isVisible =>
            {
                if (isVisible)
                {
                    (_window ??= _serviceProvider.GetRequiredService<SuspendedWindowsWindow>()).Show();
                }
                else
                {
                    _window?.Hide();
                }
            });
    }

    private static IObservable<bool> GetVisibilityChanges(
        WindowSelectorViewModel selectorViewModel,
        IProcessSuspensionService suspensionService,
        WinTabberEventManager eventManager,
        GeneralSettings settings)
    {
        // Original behavior: show while the switcher is up and something is actually suspended.
        var followsSwitcher = selectorViewModel.IsSwitcherActiveChanges.CombineLatest(
            suspensionService.HasSuspendedChanges,
            (active, has) => active && has);

        // CmdSuspendedWindows pins the window open independently of the switcher; pressing it again
        // unpins. Seeded with StartWith(false) so the combined stream still emits when the command is
        // never used, leaving the behavior above untouched.
        var pinnedOpen = eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.CmdSuspendedWindows && settings.EnableWindowSuspension)
            .Scan(false, (isPinned, _) => !isPinned)
            .StartWith(false);

        return followsSwitcher
            .CombineLatest(pinnedOpen, (visibleWithSwitcher, isPinned) => visibleWithSwitcher || isPinned)
            // Read on the next switcher open or pin, same as the per-tile sleep button: this does not
            // re-check mid-session, so a bar already open when the feature turns off stays open until
            // the next visibility change.
            .Select(isVisible => isVisible && settings.EnableWindowSuspension)
            .DistinctUntilChanged();
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
