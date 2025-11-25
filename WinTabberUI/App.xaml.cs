using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinTabber.Events;
using System.Windows.Input;
using WinTabberUI.Services;
using WinTabberUI.Models.Settings;

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

    protected override void OnStartup(StartupEventArgs e)
    {
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
