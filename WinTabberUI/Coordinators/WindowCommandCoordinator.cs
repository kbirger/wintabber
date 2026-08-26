using System.Reactive.Linq;
using WinTabber.API;
using WinTabber.API.Suspension;
using WinTabber.Events;

namespace WinTabberUI.Coordinators;
public class WindowCommandCoordinator : IDisposable
{
    private IDisposable _subscription;

    public WindowCommandCoordinator(
        WinTabberEventManager eventManager,
        WindowManager windowManager,
        IProcessSuspensionService suspensionService)
    {
        ArgumentNullException.ThrowIfNull(SynchronizationContext.Current);
        _subscription = eventManager.CommandEvents
            .Where(evt => evt.Type.IsOneOf(EventType.CmdMinimizeWindow, EventType.CmdMaximizeWindow, EventType.CmdSuspendWindow))
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe(e =>
            {
                switch (e.Type)
                {
                    case EventType.CmdMinimizeWindow:
                        windowManager.CurrentWindow()?.Minimize();
                        break;
                    case EventType.CmdMaximizeWindow:
                        windowManager.CurrentWindow()?.Maximize();
                        break;
                    case EventType.CmdSuspendWindow:
                        // Goes through the same Suspend(WindowRef) entry point as the switcher's
                        // per-window command, so CanSuspend (elevated / own process / already
                        // suspended) is enforced identically on both paths.
                        var window = windowManager.CurrentWindow();
                        if (window is not null)
                        {
                            suspensionService.Suspend(window);
                        }
                        break;
                }
            });
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
