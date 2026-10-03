using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Api.Windowing;
using WinTabber.Api.Windowing.Suspension;
using WinTabber.Events;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Ported from the WPF app's own <see cref="WindowCommandCoordinator"/>: handles the global
/// minimize/maximize/suspend/close-application-windows hotkeys. Framework-free logic, unchanged
/// from the WPF original except for the thread-marshal: that one gated its subscription on
/// <c>SynchronizationContext.Current</c> (a WPF <c>Dispatcher</c> idiom), this one uses
/// <c>RxApp.MainThreadScheduler</c> instead, matching every other coordinator already ported in
/// this app (e.g. <see cref="WindowSelectorWindowCoordinator"/>).
/// </summary>
public class WindowCommandCoordinator : IDisposable
{
    private readonly IDisposable _subscription;
    private readonly GeneralSettings _settings;

    public WindowCommandCoordinator(
        WinTabberEventManager eventManager,
        WindowManager windowManager,
        IProcessSuspensionService suspensionService,
        ApplicationSettings settings)
    {
        _settings = settings.General;
        _subscription = eventManager.CommandEvents
            .Where(evt =>
                evt.Type.IsOneOf(
                    EventType.CmdMinimizeWindow,
                    EventType.CmdMaximizeWindow,
                    EventType.CmdSuspendWindow,
                    EventType.CmdCloseApplicationWindows
                )
            )
            .ObserveOn(RxApp.MainThreadScheduler)
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
                        if (!_settings.EnableCloseApplicationWindows)
                        {
                            break;
                        }
                        var currentWindow = windowManager.CurrentWindow();
                        if (currentWindow is not null)
                        {
                            var application = currentWindow.Process.Application;
                            application.CloseAllWindows(application.GetWindows());
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
