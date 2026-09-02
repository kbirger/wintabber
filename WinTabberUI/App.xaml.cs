using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using WinTabber.Events;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private WinTabberEventManager? _eventManager;
    private IDisposable? _cleanUp;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        _eventManager?.SendEvent(EventType.CmdAppHide);
        base.OnDeactivated(e);
    }

    /// <summary>
    /// Sends Debug.WriteLine output to the file that check.ps1 clears before each run. Without a
    /// listener the trace is visible only under a debugger, and the harness cannot show it.
    /// </summary>
    [Conditional("DEBUG")]
    private static void AttachTraceLog()
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "wintabber-trace.log");
            Trace.Listeners.Add(new TextWriterTraceListener(path));
            Trace.AutoFlush = true;
        }
        catch (IOException)
        {
            // A trace file is a diagnostic aid. The application must start without it.
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AttachTraceLog();
        IServiceProvider serviceProvider = Bootstrapper.Init(this);

        _cleanUp = serviceProvider.GetRequiredService<BackgroundServiceContainer>();
        _eventManager = serviceProvider.GetRequiredService<WinTabberEventManager>();
        var startupService = serviceProvider.GetRequiredService<AutoStartupService>();

        var settings = ApplicationSettings.Load();

        base.OnStartup(e);
    }

    

    protected override void OnExit(ExitEventArgs e)
    {
        _cleanUp?.Dispose();
    }
}
