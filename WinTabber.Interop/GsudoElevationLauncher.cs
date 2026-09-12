using System.ComponentModel;
using System.Diagnostics;

namespace WinTabber.Interop;

/// <summary>
/// Launches the bundled elevator via gsudo instead of a direct runas prompt, so repeat elevations
/// within a short window reuse gsudo's own credentials cache instead of prompting every time.
/// Requires gsudo (https://github.com/gerardog/gsudo) on PATH — <see cref="IsAvailable" /> reports
/// whether it's found there.
/// </summary>
public class GsudoElevationLauncher : IElevationLauncher
{
    private bool _cacheStarted;

    public bool IsAvailable => TryResolveGsudoPath() is not null;

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)
    {
        var handleList = handles as IReadOnlyCollection<int> ?? handles.ToList();
        if (handleList.Count == 0)
        {
            return;
        }

        var gsudoPath = TryResolveGsudoPath();
        if (gsudoPath is null)
        {
            return;
        }

        EnsureCacheStarted(gsudoPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = gsudoPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "WinTabber.Elevator.exe"));
        startInfo.ArgumentList.Add(action.ToString().ToLowerInvariant());
        foreach (var handle in handleList)
        {
            startInfo.ArgumentList.Add(handle.ToString());
        }

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // gsudo declined, disappeared between the availability check and here, or failed to
            // launch for some other reason. Same silent end state as the built-in path.
        }
    }

    /// <summary>
    /// Starts a short-lived (30 second) gsudo credentials-cache session, once per process
    /// lifetime, so repeat elevations within that window don't each show their own UAC prompt.
    /// Deliberately shorter than gsudo's 5-minute default (<c>gsudo config CacheDuration</c>) to
    /// bound how long a compromised process in this app's process tree could silently elevate.
    /// </summary>
    private void EnsureCacheStarted(string gsudoPath)
    {
        if (_cacheStarted)
        {
            return;
        }

        try
        {
            var cacheStartInfo = new ProcessStartInfo
            {
                FileName = gsudoPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            cacheStartInfo.ArgumentList.Add("cache");
            cacheStartInfo.ArgumentList.Add("on");
            cacheStartInfo.ArgumentList.Add("-d");
            cacheStartInfo.ArgumentList.Add("30");

            using var process = Process.Start(cacheStartInfo);
            process?.WaitForExit();
            _cacheStarted = true;
        }
        catch (Win32Exception)
        {
            // Couldn't start the cache session (e.g. declined). Leave _cacheStarted false so the
            // next call tries again; the RunElevated call that follows still works — it just also
            // shows gsudo's own (uncached) prompt for this one action.
        }
    }

    private static string? TryResolveGsudoPath()
    {
        // gsudo's own install docs confirm this is the detection mechanism: "No Windows service is
        // required or system change is done, except adding gsudo to the PATH."
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var candidate = Path.Combine(dir, "gsudo.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
