using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Interop;
using WinTabber.ViewModels;
using WinTabberUI.Views;
using WinUIEx;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows or hides the singleton <see cref="WindowSelectorWindow"/> whenever
/// <see cref="WindowSelectorViewModel.IsSwitcherActiveChanges"/> changes. Follows
/// <see cref="MediaControlsWindowCoordinator"/>'s established shape, not the WPF original's
/// <c>ViewCoordinatorBase&lt;T&gt;</c> (not ported in this migration -- see that coordinator's own
/// doc comment).
/// </summary>
/// <remarks>
/// Singleton, matching the WPF original's <c>WindowSelectorViewCoordinator</c>, which explicitly
/// sets <c>ReuseInstances = true</c>: this is a single global switcher shown/hidden repeatedly,
/// not rebuilt per show.
/// <para>
/// The hotkey/tray-click arrives through a global hook, so this process holds no foreground right
/// and <c>Show()</c> alone would not bring the window to the front -- ported from
/// <see cref="MediaControlsWindowCoordinator"/>'s own <c>ForceForeground</c> step.
/// </para>
/// </remarks>
public class WindowSelectorWindowCoordinator : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWindowInterop _interop;
    private readonly IDisposable _subscription;
    private WindowSelectorWindow? _window;

    public WindowSelectorWindowCoordinator(
        WindowSelectorViewModel vm,
        IServiceProvider serviceProvider,
        IWindowInterop interop)
    {
        _serviceProvider = serviceProvider;
        _interop = interop;

        _subscription = vm.IsSwitcherActiveChanges
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isActive =>
            {
                if (isActive)
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
        _window ??= _serviceProvider.GetRequiredService<WindowSelectorWindow>();
        _window.ShowWindowSelector();

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
