using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace WinTabber.Common.Util;

public static class ProcessExtensions
{
    extension(Process process)
    {
        public static bool TryGetProcessById(
            int processId,
            [MaybeNullWhen(false)]
            [NotNullWhen(true)]
            out Process processResult
        )
        {
            try
            {
                processResult = Process.GetProcessById(processId);

                return true;
            }
            catch
            {
                processResult = null;
                return false;
            }
        }
    }
}
