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
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();
    }
}
