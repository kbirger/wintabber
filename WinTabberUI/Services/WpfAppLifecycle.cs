using System.Windows;

namespace WinTabberUI.Services;

public sealed class WpfAppLifecycle : IAppLifecycle
{
    private readonly Application _application;

    public WpfAppLifecycle(Application application)
    {
        _application = application;
    }

    public void Shutdown() => _application.Shutdown();
}
