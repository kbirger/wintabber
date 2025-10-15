using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Timers;
using System.Windows;
using WinTabber.API;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabberUI.Windowing;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{

    internal DockWindow _dock;
    //System.Timers.Timer _timer = new(1000);
    internal WindowManager wm = new WindowManager(new InteropProxy());
    protected override void OnActivated(EventArgs e)
    {
        var area = DesktopHelper.GetDesktopArea();
        base.OnActivated(e);

    }

    protected override void OnStartup(StartupEventArgs e)
    {
        //_timer.Enabled = true;
        //_timer.AutoReset = true;
        //_timer.Elapsed += _timer_Elapsed;
        WinTabberEventManager.Instance
            .Events
            .Where(evt => evt.type == EventType.ForegroundChanged)
            .Subscribe(evt =>
            {
                var app = wm.GetCurrentApplication();
                if (app is null)
                {
                    return;
                }

                if (app.ProcessName == Process.GetCurrentProcess().ProcessName)
                {
                    return;
                }
                if (_dock.ApplicationName != app.ProcessName)
                {
                    _dock.ApplicationName = app.ProcessName;
                }
            });
        _dock = new DockWindow();
        //_dock.Show();
        base.OnStartup(e);
    }
    private void _timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        _dock.Dispatcher.Invoke(() =>
        {
            var app = wm.GetCurrentApplication();
            if (app is null)
            {
                return;
            }

            if (app.ProcessName == Process.GetCurrentProcess().ProcessName)
            {
                return;
            }
            _dock.ApplicationName = app.ProcessName;
        });

    }

    protected override void OnExit(ExitEventArgs e)
    {
        //_hook?.Dispose();
    }
}
