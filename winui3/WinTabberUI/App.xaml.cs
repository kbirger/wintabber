using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WinTabber.Api.Windowing.Thumbnails;
using WinTabber.Events;
using WinTabber.ViewModels;
using WinTabberUI.Coordinators;
using WinTabberUI.Views;

namespace WinTabberUI;

public partial class App : Application
{
    private Window? _window;

    // Rooted explicitly, not left to whatever gets resolved transitively through _window's own
    // constructor chain -- see AddThumbnailWindowGraph's doc comment. WinTabberEventManager is
    // resolved here for the same reason even though SettingsWindow's own SettingsViewModel dependency
    // already resolves it today: that's an incidental path, not a guarantee, and this app's global
    // hotkey pipeline (InputListenerService, wired from WinTabberEventManager's own constructor)
    // must not depend on which window happens to be shown first.
    private WinTabberEventManager? _eventManager;
    private ThumbnailWindowCoordinator? _thumbnailWindowCoordinator;

    public static ServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = Bootstrapper.Init();

        _eventManager = Services.GetRequiredService<WinTabberEventManager>();
        _thumbnailWindowCoordinator = Services.GetRequiredService<ThumbnailWindowCoordinator>().Init();

        // KNOWN GAP, disclosed rather than silently omitted: the WPF original disposes
        // IWindowThumbnailService from its own OnExit override (restoring any window still
        // thumbnailed away, so exiting the app never strands one off-screen with no UI left to bring
        // it back). Microsoft.UI.Xaml.Application has no OnExit-equivalent override in this SDK
        // version for an unpackaged desktop app, and this migration has not yet designed its WinUI 3
        // app-exit/shutdown lifecycle at all (every window ported so far is still reached only via
        // this temporary direct-launch OnLaunched, not a real tray-app background lifetime) -- that
        // is a separate, larger concern than this task's scope. ProcessExit is the nearest available
        // hook and is wired here so the restore behavior exists rather than silently not happening,
        // but it is not a substitute for a real app-exit design once more windows/coordinators land.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            Services.GetRequiredService<IWindowThumbnailService>().Dispose();

        _window = new SettingsWindow(Services.GetRequiredService<SettingsViewModel>());
        _window.Activate();
    }
}
