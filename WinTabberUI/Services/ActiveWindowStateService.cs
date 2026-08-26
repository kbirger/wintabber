using System.Reactive.Linq;
using WinTabber.API;
using WinTabber.Events;

namespace WinTabberUI.Services;

public partial class ActiveWindowStateService(WinTabberEventManager eventManager, WindowManager windowManager) : IActiveWindowStateService
{
    private readonly WinTabberEventManager _eventManager = eventManager;
    private readonly WindowManager _windowManager = windowManager;


    [Lazy]
    private IObservable<ApplicationRef?> GetApplicationChanges()
    {
        return _eventManager.ApplicationChange
            .Select(data => _windowManager.GetApplication(data.Arg))
            .Where(applicationRef => applicationRef is null || (applicationRef.IsValidProcess && applicationRef.CurrentWindow() is { }))
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();
    }

    [Lazy]
    private IObservable<WindowRef?> GetWindowChanges()
    {
        return _eventManager.WindowChange
            .Select(data => _windowManager.GetWindow(data.Arg))
            .Where(windowRef => windowRef is null || windowRef.IsValidUserWindow && windowRef.Process.IsValid)
            // Activation history is recorded here, upstream of the Replay, so that every subscriber
            // observes a history which already includes this change. Recording it from a subscriber
            // instead made window ordering depend on subscription order: the window selector was
            // constructed first, so its GetWindows() call read the history as of one foreground
            // change ago and its tiles were ordered one step behind.
            .Do(windowRef =>
            {
                if (windowRef is not null)
                {
                    _windowManager.RegisterForegroundWindowChanged(windowRef.Handle);
                }
            })
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();
    }
}
