using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Interop;
using WinTabber.UI.Media.Services;
using WinTabberUI.Views;
using WinUIEx;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows or hides the singleton <see cref="MediaControlsWindow"/> whenever
/// <see cref="IMediaControlsStateService.IsMediaControlsVisibleChanges"/> changes. The WPF original
/// (<c>MediaWindowViewCoordinator</c>) is a <c>ViewCoordinatorBase&lt;T&gt;</c> subclass with a fade
/// animation and a retry-activate dispatcher loop; neither exists in winui3 yet, and this migration's
/// other coordinators (<see cref="ThumbnailWindowCoordinator"/>) are plain classes reacting to a
/// change stream directly, so this follows that simpler, already-established shape instead of
/// porting <c>ViewCoordinatorBase&lt;T&gt;</c> for a single consumer.
/// </summary>
/// <remarks>
/// <see cref="MediaControlsWindow"/> is registered as a singleton (not transient, unlike
/// <c>WindowSelectorWindow</c>/<c>ThumbnailWindow</c>): the WPF coordinator explicitly sets
/// <c>ReuseInstances = true</c> and only ever calls <c>Show()</c>/<c>Hide()</c> on one instance,
/// never disposing it, matching a window meant to reappear instantly on every hotkey press rather
/// than rebuild its whole session list each time.
/// <para>
/// The hotkey arrives through a global hook, so this process holds no foreground right and
/// <c>Show()</c> alone would not bring the window to the front -- ported from the WPF coordinator's
/// own <c>ForceForeground</c> step, using the same shared, framework-agnostic <see cref="IWindowInterop"/>.
/// NOT ported: the WPF original's Dispatcher.BeginInvoke retry-Activate()-if-not-active dance. This
/// is unverified against real behavior (no live test of this window's hotkey path has happened
/// yet) -- if the window shows without taking focus, that retry is the first thing to restore.
/// </para>
/// </remarks>
public class MediaControlsWindowCoordinator : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWindowInterop _interop;
    private readonly IDisposable _subscription;
    private MediaControlsWindow? _window;

    public MediaControlsWindowCoordinator(
        IMediaControlsStateService stateService,
        IServiceProvider serviceProvider,
        IWindowInterop interop)
    {
        _serviceProvider = serviceProvider;
        _interop = interop;

        _subscription = stateService
            .IsMediaControlsVisibleChanges.ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isVisible =>
            {
                if (isVisible)
                {
                    ShowWindow();
                }
                else
                {
                    _window?.Hide();
                }
            });
    }

    private void ShowWindow()
    {
        _window ??= _serviceProvider.GetRequiredService<MediaControlsWindow>();
        _window.Show();

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (handle == nint.Zero)
        {
            return;
        }

        _interop.ForceForeground((int)handle);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
