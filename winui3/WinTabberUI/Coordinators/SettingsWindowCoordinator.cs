using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Interop;
using WinTabber.ViewModels;
using WinTabberUI.Views;
using WinUIEx;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Shows or hides <see cref="SettingsWindow"/> whenever <see cref="SettingsViewModel.IsSettingsShown"/>
/// changes. Follows <see cref="MediaControlsWindowCoordinator"/>'s established shape.
/// </summary>
/// <remarks>
/// Transient, matching the WPF original's <c>SettingsWindowViewCoordinator</c>, which explicitly
/// sets <c>ReuseInstances = false</c>: a fresh window each time, not reused like the switcher.
/// <para>
/// Unlike <see cref="WindowSelectorWindow"/>, the user can close this window directly via its own
/// titlebar. Without the <see cref="Window.Closed"/> handler below, <c>IsSettingsShown</c> would
/// stay stuck <c>true</c> after such a close -- ported from the WPF original's own
/// <c>Instance_Closed</c> handler, not new scope.
/// </para>
/// </remarks>
public class SettingsWindowCoordinator : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWindowInterop _interop;
    private readonly SettingsViewModel _vm;
    private readonly IDisposable _subscription;
    private SettingsWindow? _window;

    public SettingsWindowCoordinator(
        SettingsViewModel vm,
        IServiceProvider serviceProvider,
        IWindowInterop interop)
    {
        _vm = vm;
        _serviceProvider = serviceProvider;
        _interop = interop;

        _subscription = vm.IsSettingsShown
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isShown =>
            {
                if (isShown)
                {
                    ShowWindow();
                }
                else
                {
                    _window?.Close();
                }
            });
    }

    private void ShowWindow()
    {
        _window = _serviceProvider.GetRequiredService<SettingsWindow>();
        _window.Closed += OnWindowClosed;
        _window.Show();

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (handle == nint.Zero)
        {
            return;
        }

        _interop.ForceForeground((int)handle);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _window!.Closed -= OnWindowClosed;
        _window = null;
        _vm.Hide();
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
