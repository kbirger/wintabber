using System.Reactive.Linq;
using WinTabber.API;
using WinTabber.API.Suspension;
using WinTabber.Events;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.Coordinators;
public class WindowCommandCoordinator : IDisposable
{
    private IDisposable _subscription;
    private readonly GeneralSettings _settings;

    public WindowCommandCoordinator(
        WinTabberEventManager eventManager,
        WindowManager windowManager,
        IProcessSuspensionService suspensionService,
        ApplicationSettings settings)
    {
        _settings = settings.General;
        ArgumentNullException.ThrowIfNull(SynchronizationContext.Current);
        _subscription = eventManager.CommandEvents
            .Where(evt =>
                evt.Type.IsOneOf(
                    EventType.CmdMinimizeWindow,
                    EventType.CmdMaximizeWindow,
                    EventType.CmdSuspendWindow,
                    EventType.CmdCloseApplicationWindows
                )
            )
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
                        if (!_settings.EnableWindowSuspension)
                        {
                            break;
                        }
                        var window = windowManager.CurrentWindow();
                        if (window is not null)
                        {
                            suspensionService.Suspend(window);
                        }
                        break;
                    case EventType.CmdCloseApplicationWindows:
                        // Same grouping WindowSelector uses to decide which windows belong to one
                        // app, so this closes exactly the set the switcher would show together.
                        var currentWindow = windowManager.CurrentWindow();
                        if (currentWindow is not null)
                        {
                            foreach (var appWindow in currentWindow.Process.Application.GetWindows())
                            {
                                appWindow.Close();
                            }
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
