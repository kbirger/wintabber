using Microsoft.UI.Dispatching;

namespace WinTabberUI.Services;

public sealed class WinUIAppLifecycle : IAppLifecycle
{
    // The services must be disposed before Exit(), not left to ProcessExit: GlobalHotKeys.Windows
    // runs its message loop on a foreground thread that only HotKeyManager.Dispose() ends, and the
    // runtime raises ProcessExit only after every foreground thread has ended. Waiting for
    // ProcessExit therefore never finishes, and the process stays alive with its windows closed.
    //
    // The disposal is queued rather than run inline because this is called from the tray menu's
    // command handler, and disposing the tray icon inside its own command would tear it down
    // while its flyout is still dispatching the click.
    public void Shutdown()
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var queue = DispatcherQueue.GetForCurrentThread();

        if (queue is null || !queue.TryEnqueue(() => ShutdownCore(app)))
        {
            ShutdownCore(app);
        }
    }

    private static void ShutdownCore(App app)
    {
        app.DisposeBackgroundServices();
        app.Exit();
    }
}
