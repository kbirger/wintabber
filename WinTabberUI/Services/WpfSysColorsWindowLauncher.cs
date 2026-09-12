namespace WinTabberUI.Services;

public sealed class WpfSysColorsWindowLauncher : ISysColorsWindowLauncher
{
    public void Show()
    {
        var sysColors = new SysColor();
        sysColors.ShowDialog();
    }
}
