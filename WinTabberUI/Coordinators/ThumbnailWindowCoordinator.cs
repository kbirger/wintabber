using System.Diagnostics;
using System.Reactive.Linq;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using WinTabber.API;
using WinTabber.API.Thumbnails;
using WinTabber.Events;
using WinTabber.Interop;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Opens a floating <see cref="ThumbnailWindow"/> whenever <see cref="IWindowThumbnailService"/> starts
/// tracking a window. This is the multi-instance analog of <c>ViewCoordinatorBase&lt;T&gt;</c>: that base
/// class only ever manages a single shared window instance, but each thumbnailed window needs its own.
/// Each <see cref="ThumbnailWindow"/> watches the service directly and closes itself when its own entry
/// disappears, so this coordinator only needs to react to additions.
/// </summary>
public class ThumbnailWindowCoordinator : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IInteropProxy _interop;
    private readonly WindowManager _windowManager;
    private readonly IDisposable _subscription;
    private readonly IDisposable _commandSubscription;

    private readonly IWindowThumbnailService _thumbnailService;

    public ThumbnailWindowCoordinator(
        IWindowThumbnailService thumbnailService,
        IServiceProvider serviceProvider,
        IInteropProxy interop,
        WindowManager windowManager,
        WinTabberEventManager eventManager)
    {
        _thumbnailService = thumbnailService;
        _serviceProvider = serviceProvider;
        _interop = interop;
        _windowManager = windowManager;

        _subscription = thumbnailService
            .Connect()
            .ObserveOnDispatcher()
            .Subscribe(OnChanges);

        _commandSubscription = eventManager
            .CommandEvents.Where(evt => evt.Type == EventType.CmdThumbnailWindow)
            .ObserveOnDispatcher()
            .Subscribe(_ => ToggleForegroundWindowThumbnail());
    }

    /// <summary>
    /// Thumbnails the foreground window, or un-thumbnails it if it already is.
    /// <para>
    /// Note the asymmetry in <see cref="IWindowThumbnailService" />: <c>StartThumbnail</c> takes a
    /// <see cref="WindowRef" /> while <c>StopThumbnail</c>/<c>IsThumbnailed</c> take a raw handle,
    /// so the start path has to go through <see cref="WindowManager" /> to resolve the HWND.
    /// </para>
    /// </summary>
    private void ToggleForegroundWindowThumbnail()
    {
        var handle = _interop.GetForegroundWindowHandle();
        if (handle == 0)
        {
            return;
        }

        if (_thumbnailService.IsThumbnailed(handle))
        {
            _thumbnailService.StopThumbnail(handle);
            return;
        }

        WindowRef? window;
        try
        {
            window = _windowManager.GetWindow(handle);
        }
        catch (Exception ex)
        {
            // The foreground window can belong to a process we cannot open (elevated, or already exiting).
            Debug.WriteLine($"ThumbnailWindowCoordinator: cannot resolve foreground window {handle}: {ex.Message}");
            return;
        }

        if (window is null || !_thumbnailService.CanThumbnail(window))
        {
            return;
        }

        _thumbnailService.StartThumbnail(window);
    }

    public ThumbnailWindowCoordinator Init() => this;

    private void OnChanges(IChangeSet<ThumbnailEntry, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Add)
            {
                try
                {
                    OpenThumbnailWindow(change.Current);
                }
                catch (Exception ex)
                {
                    // Window creation failed after the source window was already moved off-screen — restore
                    // it rather than stranding it invisibly with no UI left to bring it back.
                    Debug.WriteLine($"ThumbnailWindowCoordinator: failed to open window for handle {change.Current.Handle}: {ex}");
                    _thumbnailService.StopThumbnail(change.Current.Handle);
                }
            }
        }
    }

    private void OpenThumbnailWindow(ThumbnailEntry entry)
    {
        string title;
        try
        {
            title = _interop.GetWindowTitle(entry.Handle);
        }
        catch
        {
            title = string.Empty;
        }

        var window = _serviceProvider.GetRequiredService<ThumbnailWindow>();
        window.Initialize(entry.Handle, title, entry.Placement.Bounds.Width, entry.Placement.Bounds.Height);
        window.Show();
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
