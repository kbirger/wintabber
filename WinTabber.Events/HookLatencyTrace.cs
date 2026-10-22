using System.Diagnostics;

namespace WinTabber.Events;

// TEMPORARY diagnostic for the desktop freeze at startup and shutdown. Remove after the investigation.
// Debug builds only: the compiler removes every call, so a release build never creates the log file.
// Writes to %TEMP%\wintabber-hook-latency.log.
public static class HookLatencyTrace
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "wintabber-hook-latency.log");
    private static readonly object Gate = new();
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(30);

    // Latency is the time between the OS timestamp of the event and the moment the managed handler ran.
    // A large value means the hook thread was blocked or suspended, and Windows held the input for it.
    [Conditional("DEBUG")]
    public static void Sample(string kind, DateTimeOffset eventTime)
    {
        var latency = DateTimeOffset.Now - eventTime;
        if (latency < Threshold)
        {
            return;
        }

        Mark(
            $"SLOW {kind} latency={latency.TotalMilliseconds:F0}ms gcPause={GC.GetTotalPauseDuration().TotalMilliseconds:F0}ms "
                + $"gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}"
        );
    }

    [Conditional("DEBUG")]
    public static void Mark(string message)
    {
        lock (Gate)
        {
            File.AppendAllText(
                LogPath,
                $"{DateTime.Now:HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}"
            );
        }
    }

    public static Stopwatch Start(string message)
    {
        Mark($"BEGIN {message}");
        return Stopwatch.StartNew();
    }

    [Conditional("DEBUG")]
    public static void End(Stopwatch watch, string message) =>
        Mark($"END {message} elapsed={watch.Elapsed.TotalMilliseconds:F0}ms");
}
