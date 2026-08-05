using WinTabber.Interop;

namespace WinTabber.API.Thumbnails;

/// <summary>
/// A window that has been moved off-screen for live thumbnail preview, along with the placement it
/// should be restored to.
/// </summary>
public sealed record ThumbnailEntry(int Handle, WindowPlacement Placement, int OriginalExStyle);
