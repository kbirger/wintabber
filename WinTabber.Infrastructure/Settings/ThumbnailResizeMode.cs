namespace WinTabberUI.Services;

public enum ThumbnailResizeMode
{
    /// <summary>Thumb only: resizing the floating preview is locked to the source's original aspect ratio — effectively just a scale factor, not a free two-dimensional resize. The real window is never touched.</summary>
    ThumbOnlyLockedAspect = 0,

    /// <summary>Thumb only: the floating preview can be resized freely (any aspect ratio), DWM stretches the bitmap to fill it. The real window is never touched.</summary>
    ThumbOnlyFreeAspect = 1,

    /// <summary>Thumb and source: resizing the floating preview also resizes the real (off-screen) window, once per drag-release, by a uniform zoom factor that preserves its original aspect ratio.</summary>
    ResizeSource = 2,
}
