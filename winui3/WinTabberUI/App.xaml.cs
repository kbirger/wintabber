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
    private MediaControlsWindowCoordinator? _mediaControlsWindowCoordinator;

    public static ServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        // STATUS_STOWED_EXCEPTION (a native fast-fail deep in WinUI 3's own plumbing) bypasses all
        // three of these -- confirmed live: the crash log stayed empty for that crash and only
        // started filling once the underlying managed exception was fixed and a second, ordinary
        // unhandled exception (RPC_E_WRONG_THREAD) surfaced instead. Kept anyway: they are the only
        // way to see an ordinary unhandled exception's full stack trace when the process is about to
        // die and there is no debugger attached.
        UnhandledException += (_, e) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wintabberui-crash.log"),
                $"[{DateTime.Now:O}] XAML UnhandledException: {e.Exception}\n");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wintabberui-crash.log"),
                $"[{DateTime.Now:O}] AppDomain UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}\n");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wintabberui-crash.log"),
                $"[{DateTime.Now:O}] UnobservedTaskException: {e.Exception}\n");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = Bootstrapper.Init();

        _eventManager = Services.GetRequiredService<WinTabberEventManager>();
        _thumbnailWindowCoordinator = Services.GetRequiredService<ThumbnailWindowCoordinator>().Init();
        _mediaControlsWindowCoordinator = Services.GetRequiredService<MediaControlsWindowCoordinator>();

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
