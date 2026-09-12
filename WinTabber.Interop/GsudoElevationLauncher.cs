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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    // Renew a little before the actual 30-second window lapses, so a call arriving just before
    // expiry doesn't race gsudo's own cache teardown and end up uncached anyway.
    private static readonly TimeSpan CacheRenewalMargin = TimeSpan.FromSeconds(5);

    private DateTime? _cacheExpiresAt;

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
    /// Starts (or renews) a short-lived gsudo credentials-cache session so repeat elevations
    /// within <see cref="CacheDuration"/> don't each show their own UAC prompt. Deliberately
    /// shorter than gsudo's 5-minute default (<c>gsudo config CacheDuration</c>) to bound how long
    /// a compromised process in this app's process tree could silently elevate. Re-runs whenever
    /// the previous cache session is at or past expiry — a one-shot flag would otherwise leave the
    /// gsudo backend prompting on every single call once the window lapses, which is worse than
    /// not caching at all.
    /// </summary>
    private void EnsureCacheStarted(string gsudoPath)
    {
        if (_cacheExpiresAt is { } expiresAt && DateTime.UtcNow < expiresAt)
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
            cacheStartInfo.ArgumentList.Add(((int)CacheDuration.TotalSeconds).ToString());

            using var process = Process.Start(cacheStartInfo);
            if (process is null)
            {
                return;
            }

            bool exited = process.WaitForExit(CacheDuration);
            if (exited && process.ExitCode == 0)
            {
                _cacheExpiresAt = DateTime.UtcNow + CacheDuration - CacheRenewalMargin;
            }
            // A non-zero exit code (e.g. the user declined the cache-start prompt) or a timed-out
            // wait leaves _cacheExpiresAt exactly where it was — null on first attempt, or its
            // prior (already-expired) value — so the next call tries again rather than being
            // permanently disabled.
        }
        catch (Win32Exception)
        {
            // Couldn't launch gsudo at all. Leave _cacheExpiresAt as-is; the RunElevated call that
            // follows still works — it just also shows gsudo's own (uncached) prompt this once.
        }
    }

    private static string? TryResolveGsudoPath()
    {
        // gsudo's own install docs confirm this is the detection mechanism: "No Windows service is
        // required or system change is done, except adding gsudo to the PATH." Checking the User
        // and Machine PATH values (read fresh from the registry on every call) in addition to this
        // process's own inherited PATH block means a gsudo install performed from within this same
        // running app (via the settings page's "Install" button) is detected immediately, without
        // needing to restart WinTabber — the process's own PATH block is captured once at launch
        // and never updated by a later installer writing to the registry.
        string?[] searchPaths =
        [
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        ];

        foreach (var pathVariable in searchPaths)
        {
            if (string.IsNullOrWhiteSpace(pathVariable))
            {
                continue;
            }

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
        }

        return null;
    }
}
