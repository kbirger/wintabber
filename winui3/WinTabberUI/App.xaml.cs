using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WinTabber.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI;

public partial class App : Application
{
    private Window? _window;
    public static ServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = Bootstrapper.Init();

        _window = new SettingsWindow(Services.GetRequiredService<SettingsViewModel>());
        _window.Activate();
    }
}
