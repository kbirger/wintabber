using System.Diagnostics;
using System.Drawing;
using System.Reactive.Linq;
using DynamicData;
using WinTabber.Interop;

namespace WinTabber.API.Thumbnails;

/// <summary>
/// Tracks windows that have been moved off-screen for live thumbnail preview and can restore them to
/// their original position. Mirrors the shape of <see cref="WinTabber.API.Suspension.IProcessSuspensionService"/>,
/// but the "hide" mechanism here is moving the window off the virtual screen (not suspending the process or
/// hiding the window), so DWM keeps compositing it and thumbnail controls keep rendering a live preview.
/// </summary>
public sealed class WindowThumbnailService : IWindowThumbnailService
{
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

    private readonly IInteropProxy _interop;
    private readonly IProcessRepository _processRepository;
    private readonly SourceCache<ThumbnailEntry, int> _cache = new(e => e.Handle);
    private readonly IDisposable _watchdog;

    public WindowThumbnailService(IInteropProxy interop, IProcessRepository processRepository)
    {
        _interop = interop;
        _processRepository = processRepository;

        // Self-restore if a thumbnailed window's source is destroyed (app closed/crashed) while it was
        // off-screen: there's no dedicated "window destroyed" event flowing through IInteropProxy, so this
        // polls the handles we're actively tracking. The set is normally empty or tiny, so this is cheap.
        _watchdog = Observable.Interval(WatchdogInterval).Subscribe(_ => PruneDestroyedWindows());
    }

    public IObservable<IChangeSet<ThumbnailEntry, int>> Connect() => _cache.Connect();

    public bool IsThumbnailed(int handle) => _cache.Lookup(handle).HasValue;

    public bool CanThumbnail(WindowRef window) =>
        !IsThumbnailed(window.Handle)
        && !IsOwnWindow(window.Handle)
        && window.State is WindowPlacement.WindowState.Normal or WindowPlacement.WindowState.Maximized;

    /// <summary>
    /// WinTabber's own windows (the switcher, the thumbnail previews themselves, settings, …) are never
    /// thumbnailable: moving them off-screen would hide the very UI that brings them back, and a preview
    /// of a preview is meaningless. Checked on the raw HWND so every entry point is covered, including
    /// the foreground-window hotkey path.
    /// </summary>
    private bool IsOwnWindow(int handle)
    {
        try
        {
            return _interop.GetWindowProcessId(handle) == _processRepository.GetCurrentProcessId();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WindowThumbnailService: cannot resolve owning process for handle {handle}: {ex.Message}");
            return false;
        }
    }

    public bool StartThumbnail(WindowRef window)
    {
        // CanThumbnail excludes Minimized/Hidden windows: they have nothing live for DWM to composite into
        // the preview. Normal and Maximized are both fine — MoveWindowOffScreen/RestoreWindowPosition round
        // -trip the real restored geometry (WindowPlacement.NormalBounds) and original state via
        // SetWindowPlacement, not just the window's current on-screen rect.
        if (!CanThumbnail(window))
        {
            return false;
        }

        try
        {
            WindowPlacement placement = _interop.MoveWindowOffScreen(window.Handle);
            int originalExStyle = _interop.HideFromTaskbar(window.Handle);
            _cache.AddOrUpdate(new ThumbnailEntry(window.Handle, placement, originalExStyle));
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WindowThumbnailService: failed to start thumbnail for handle {window.Handle}: {ex}");
            return false;
        }
    }

    public void Resize(int handle, int width, int height)
    {
        var lookup = _cache.Lookup(handle);
        if (!lookup.HasValue)
        {
            return;
        }

        try
        {
            _interop.ResizeWindow(handle, width, height);

            // Keep the original off-screen position but remember the new size, so restoring later lands
            // the window at the size the user resized the preview to, not the size it had before thumbnailing.
            var placement = lookup.Value.Placement;
            var bounds = placement.Bounds;
            var updatedPlacement = new WindowPlacement
            {
                State = placement.State,
                Bounds = new Rectangle(bounds.X, bounds.Y, width, height),
            };
            _cache.AddOrUpdate(new ThumbnailEntry(handle, updatedPlacement, lookup.Value.OriginalExStyle));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WindowThumbnailService: failed to resize handle {handle}: {ex}");
        }
    }

    public bool StopThumbnail(int handle)
    {
        var lookup = _cache.Lookup(handle);
        if (!lookup.HasValue)
        {
            return false;
        }

        _cache.Remove(handle);

        try
        {
            // Restore the taskbar-visibility style first, while the window is still off-screen: the
            // hide/show cycle that forces Explorer to refresh the taskbar button is only invisible to the
            // user if it happens before the window moves back into view.
            _interop.RestoreExtendedStyle(handle, lookup.Value.OriginalExStyle);
            _interop.RestoreWindowPosition(handle, lookup.Value.Placement);
            _interop.BringWindowToFront(handle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WindowThumbnailService: failed to restore handle {handle}: {ex}");
        }

        return true;
    }

    public void RestoreAll()
    {
        foreach (var entry in _cache.Items.ToList())
        {
            try
            {
                StopThumbnail(entry.Handle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowThumbnailService: RestoreAll failed for handle {entry.Handle}: {ex}");
            }
        }
    }

    private void PruneDestroyedWindows()
    {
        foreach (var entry in _cache.Items.ToList())
        {
            try
            {
                if (!_interop.IsWindow(entry.Handle))
                {
                    // The source window is gone; there's nothing left to restore, just stop tracking it.
                    _cache.Remove(entry.Handle);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowThumbnailService: watchdog check failed for handle {entry.Handle}: {ex}");
            }
        }
    }

    public void Dispose()
    {
        _watchdog.Dispose();
        RestoreAll();
        _cache.Dispose();
    }
}
