using Windows.Win32;
using Windows.Win32.Foundation;

foreach (var arg in args)
{
    if (!int.TryParse(arg, out var handle))
    {
        continue;
    }

    var hwnd = new HWND(handle);
    if (PInvoke.IsWindow(hwnd))
    {
        PInvoke.PostMessage(hwnd, PInvoke.WM_CLOSE, 0, 0);
    }
}
