using System.Reactive.Linq;
using WinTabber.API;
using WinTabber.Events;
using WinTabberUI.Extensions;

namespace WinTabberUI.Coordinators;
public class WindowCommandCoordinator : IDisposable
{
    private IDisposable _subscription;

    public WindowCommandCoordinator(WinTabberEventManager eventManager, WindowManager windowManager)
    {
        ArgumentNullException.ThrowIfNull(SynchronizationContext.Current);
        _subscription = eventManager.CommandEvents
            .Where(evt => evt.Type.IsOneOf(EventType.CmdMinimizeWindow, EventType.CmdMaximizeWindow))
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
                }
            });
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}