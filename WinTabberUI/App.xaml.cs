using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Serilog;
using WinTabberUI.Views;

namespace WinTabberUI;

public partial class App : Application
{
    // Rooted explicitly via BackgroundServiceContainer, not left to whatever gets resolved
    // transitively through some window's own constructor chain -- see that container's own doc
    // comment and AddThumbnailWindowGraph's.
    private BackgroundServiceContainer? _backgroundServices;

    public static ServiceProvider Services { get; private set; } = null!;

    public App()
    {
        AppLogging.Init();
        InitializeComponent();

        // STATUS_STOWED_EXCEPTION (a native fast-fail deep in WinUI 3's own plumbing) bypasses all
        // three of these -- confirmed live: the crash log stayed empty for that crash and only
        // started filling once the underlying managed exception was fixed and a second, ordinary
        // unhandled exception (RPC_E_WRONG_THREAD) surfaced instead. Kept anyway: they are the only
        // way to see an ordinary unhandled exception's full stack trace when the process is about to
        // die and there is no debugger attached.
        UnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, "XAML UnhandledException: {Message}", e.Message);
            Log.CloseAndFlush();
            AppLogging.BreakIntoDebugger();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(
                e.ExceptionObject as Exception,
                "AppDomain UnhandledException (terminating={IsTerminating}): {ExceptionObject}",
                e.IsTerminating,
                e.ExceptionObject
            );
            Log.CloseAndFlush();
            AppLogging.BreakIntoDebugger();
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "UnobservedTaskException");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Must stay false: AccessKeyBadgeLayer (WinTabber.UI.Common.AccessKeys) draws its own
        // custom badge for every AccessKeyDisplayRequested. Leaving this true would draw the
        // framework's own default badge on top of it.
        AccessKeyManager.AreKeyTipsEnabled = false;

        Services = Bootstrapper.Init();

        _backgroundServices = Services.GetRequiredService<BackgroundServiceContainer>();

        // Microsoft.UI.Xaml.Application has no OnExit-equivalent override in this SDK version for an
        // unpackaged desktop app, so ProcessExit is the nearest available hook -- same reasoning
        // BackgroundServiceContainer's own doc comment gives for what it disposes and in what order.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _backgroundServices?.Dispose();
            Log.Information("WinTabber exiting");
            Log.CloseAndFlush();
        };

        // No window is shown at launch: the app starts quietly in the tray. SettingsWindow and
        // WindowSelectorWindow are now shown on demand, driven by WindowSelectorWindowCoordinator/
        // SettingsWindowCoordinator reacting to their view models' own IObservable<bool> signals.
    }
}
