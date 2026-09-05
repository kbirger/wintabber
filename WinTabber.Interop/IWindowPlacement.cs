namespace WinTabber.Interop;

public interface IWindowPlacement
{
    /// <summary>
    /// Captures the window's current placement (via <see cref="IWindowInterop.GetWindowPlacement"/>) and moves it to a
    /// screen-space rectangle guaranteed to be outside every monitor's bounds, keeping its size unchanged.
    /// The window is not hidden (no SW_HIDE/SW_SHOW change) so DWM keeps compositing it and thumbnail
    /// previews keep rendering live. Returns the captured placement so the caller can restore it later.
    /// </summary>
    WindowPlacement MoveWindowOffScreen(int handle);

    /// <summary>
    /// Restores a window to <paramref name="placement"/>'s captured state via SetWindowPlacement (not
    /// SetWindowPos): this re-applies the original showCmd (Normal/Maximized/Minimized) together with
    /// rcNormalPosition in one atomic call, so a window that was maximized when thumbnailed comes back
    /// maximized (on the right monitor) instead of landing as an ordinary window sized to the whole screen.
    /// </summary>
    void RestoreWindowPosition(int handle, WindowPlacement placement);

    /// <summary>
    /// Changes only the window's size (its position, including its off-screen thumbnail position, is left
    /// alone). No-op if the handle is not a window.
    /// </summary>
    void ResizeWindow(int handle, int width, int height);

    /// <summary>
    /// Hides the window's taskbar button (sets WS_EX_TOOLWINDOW, clears WS_EX_APPWINDOW) and returns the
    /// original extended style so it can be restored later via <see cref="RestoreExtendedStyle"/>. Only
    /// call this while the window is positioned off-screen: forcing Explorer to notice the taskbar change
    /// requires a brief hide/show cycle, which would otherwise be a visible flicker. No-op (returns 0) if
    /// the handle is not a window.
    /// </summary>
    int HideFromTaskbar(int handle);

    /// <summary>Restores a previously-captured extended style (see <see cref="HideFromTaskbar"/>). Same off-screen-only caveat applies. No-op if the handle is not a window.</summary>
    void RestoreExtendedStyle(int handle, int originalExStyle);
}
