using System.Runtime.InteropServices;

namespace WinTabber.Interop;

/// <summary>
/// Undocumented user32 export used to set window chrome attributes (e.g. blur-behind). Not present
/// in CsWin32 metadata. Lives here per CLAUDE.md's Windows Interop rule: hand-written declarations
/// for undocumented APIs live in WinTabber.Interop regardless of what they act on.
/// </summary>
public static class ChromeInterop
{
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static void SetWindowCompositionAttribute(IntPtr hwnd, WindowCompositionAttributeData data) =>
        SetWindowCompositionAttribute(hwnd, ref data);
}
