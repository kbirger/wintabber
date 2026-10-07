using System.Diagnostics;
using Serilog;

namespace WinTabberUI;

/// <summary>
/// Configures the Serilog global logger. Every process start writes its own file: the start
/// timestamp is part of the file name, and <see cref="RollingInterval.Day"/> appends the date, so a
/// process that runs past midnight rolls to a new file for the new day.
/// </summary>
internal static class AppLogging
{
    private const int RetentionDays = 14;

    public static string LogDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTabber", "logs");

    public static void Init()
    {
        var startStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("Pid", Environment.ProcessId)
            .WriteTo.File(
                Path.Combine(LogDirectory, $"wintabber-{startStamp}-.log"),
                rollingInterval: RollingInterval.Day,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        DeleteOldLogs();
        Log.Information("WinTabber started. Pid {Pid}, version {Version}", Environment.ProcessId, typeof(AppLogging).Assembly.GetName().Version);
    }

    /// <summary>
    /// Debug builds only: attaches a debugger, or breaks if one is attached, so the failing state is
    /// visible before the process terminates. Release builds do nothing.
    /// </summary>
    [Conditional("DEBUG")]
    public static void BreakIntoDebugger()
    {
        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }
        else
        {
            Debugger.Launch();
        }
    }

    // Serilog's own retention only counts files that share this run's name prefix, and the prefix
    // is unique per run. Old runs must be deleted by age instead.
    private static void DeleteOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "wintabber-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete old log files");
        }
    }
}
