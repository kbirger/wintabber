namespace WinTabberUI.Services;

public sealed class WinUIAppLifecycle : IAppLifecycle
{
    public void Shutdown() => Microsoft.UI.Xaml.Application.Current.Exit();
}
