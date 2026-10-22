using System.Diagnostics;

namespace WinTabber.UI.Common.AccessKeys;

// TEMPORARY diagnostic. Remove after the item-hint investigation.
// Debug builds only: the compiler removes every call to Write in other configurations, together with
// the evaluation of its arguments, so a release build never creates or appends to the log file.
internal static class AkTrace
{
    private static readonly string Path_ = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wintabber-accesskeys.log");
    private static readonly object Gate = new();

    [Conditional("DEBUG")]
    public static void Write(string message)
    {
        lock (Gate)
        {
            //System.IO.File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
    }
}
