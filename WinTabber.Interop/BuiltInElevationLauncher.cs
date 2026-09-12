using System.ComponentModel;
using System.Diagnostics;

namespace WinTabber.Interop;

/// <summary>
/// Launches the bundled <c>WinTabber.Elevator</c> helper elevated via <c>ShellExecute</c>/<c>runas</c>,
/// one process per call, batching every handle into a single invocation (one UAC prompt per call).
/// Always available — it's our own bundled binary, not an external dependency.
/// </summary>
public class BuiltInElevationLauncher : IElevationLauncher
{
    public bool IsAvailable => true;

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles)
    {
        var handleList = handles as IReadOnlyCollection<int> ?? handles.ToList();
        if (handleList.Count == 0)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "WinTabber.Elevator.exe"),
                UseShellExecute = true,
                Verb = "runas",
            };

            startInfo.ArgumentList.Add(action.ToString().ToLowerInvariant());
            foreach (var handle in handleList)
            {
                startInfo.ArgumentList.Add(handle.ToString());
            }

            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // UAC declined (ERROR_CANCELLED), or the elevator binary is missing/broken. Same end
            // state either way: these windows simply stay open.
        }
    }
}
