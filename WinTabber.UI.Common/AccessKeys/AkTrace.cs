namespace WinTabber.UI.Common.AccessKeys;

// TEMPORARY diagnostic. Remove after the item-hint investigation.
internal static class AkTrace
{
    private static readonly string Path_ = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wintabber-accesskeys.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            System.IO.File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
    }
}
