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
    internal MediaControlsWindow? _mediaWindow;
    //System.Timers.Timer _timer = new(1000);
    internal WindowManager wm = new WindowManager(new InteropProxy());
    protected override void OnActivated(EventArgs e)
    {
        var area = DesktopHelper.GetDesktopArea();
        base.OnActivated(e);

    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        //WinTabberEventManagerThreadHost.Instance.SendEvent(EventType.AppHide);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        //_timer.Enabled = true;
        //_timer.AutoReset = true;
        //_timer.Elapsed += _timer_Elapsed;
        if (SynchronizationContext.Current is null)
        {
            throw new InvalidOperationException();
        }
        _dock = new DockWindow();
        var currentProcess = Process.GetCurrentProcess().ProcessName;
        var mgr = WinTabberEventManagerThreadHost.Instance;
        mgr.ApplicationChange
        .ObserveOn(SynchronizationContext.Current)
        .Where(evt => evt.Arg != currentProcess)
        .Subscribe(evt =>
        {
            if (_dock.ApplicationName != evt.Arg)
            {
                _dock.ApplicationName = evt.Arg;
            }
        });

        mgr.WindowChange
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe(e =>
            {
                wm.RegisterForegroundWindowChanged(e.Arg.Handle);
            });

        mgr.CommandEvents
            .ObserveOn(SynchronizationContext.Current)
            .Where(evt => evt.Type == EventType.DockWindow)
            .Subscribe(evt => _dock.Show());

        mgr.CommandEvents
            .ObserveOn(SynchronizationContext.Current)
            .Where(evt => evt.Type == EventType.MediaWindow)
            .Subscribe(evt =>
            {
                if (_mediaWindow is null)
                {
                    _mediaWindow = new MediaControlsWindow();
                    _mediaWindow.Closed += _mediaWindow_Closed;

                    _mediaWindow.Show();
                }
                else
                {
                    _mediaWindow?.Close();
                    _mediaWindow = null;
                }

            });
        base.OnStartup(e);
    }

    private void _mediaWindow_Closed(object? sender, EventArgs e)
    {
        _mediaWindow = null;
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
