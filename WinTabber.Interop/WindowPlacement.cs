namespace WinTabber.Interop;

public class WindowPlacement
{
    /// <summary>
    /// The window's current effective visual bounds: for Maximized/Minimized this is a rect synthesized
    /// from the primary monitor (matching what's actually on screen right now), not the restored geometry.
    /// Use this when you need "where does the window appear right now".
    /// </summary>
    public Rectangle Bounds { get; init; }

    /// <summary>
    /// The window's restored (non-maximized, non-minimized) geometry — Win32's rcNormalPosition — captured
    /// regardless of the window's current <see cref="State"/>. Use this together with <see cref="State"/>
    /// when you need to faithfully restore a window later (see IInteropProxy.RestoreWindowPosition).
    /// </summary>
    public Rectangle NormalBounds { get; init; }

    public WindowState State { get; init; }
    public enum WindowState
    {
        Normal,
        Minimized,
        Maximized,
        Hidden
    }
}
