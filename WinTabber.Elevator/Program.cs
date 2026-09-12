using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

if (args.Length < 2)
{
    return;
}

var action = args[0];

for (var i = 1; i < args.Length; i++)
{
    if (!int.TryParse(args[i], out var handle))
    {
        continue;
    }

    var hwnd = new HWND(handle);
    if (!PInvoke.IsWindow(hwnd))
    {
        continue;
    }

    switch (action)
    {
        case "close":
            PInvoke.PostMessage(hwnd, PInvoke.WM_CLOSE, 0, 0);
            break;
        case "minimize":
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
            break;
    }
}
