using System.Runtime.InteropServices;

namespace WinTabber.Interop;

/// <summary>
/// Undocumented NT APIs that are not present in CsWin32 metadata. These are the same
/// exports used by tools such as PsSuspend to suspend/resume all threads of a process atomically.
/// </summary>
internal static class NtNativeMethods
{
    [DllImport("ntdll.dll")]
    public static extern uint NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    public static extern uint NtResumeProcess(IntPtr processHandle);
}
