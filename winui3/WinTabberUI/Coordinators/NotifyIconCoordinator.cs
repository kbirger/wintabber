using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.ViewModels;

namespace WinTabberUI.Coordinators;

/// <summary>
/// Ported from the WPF app's NotifyIconCoordinator. The menu is built directly in code rather
/// than loaded from a XAML resource dictionary -- see the "Deviation" note in this plan's Global
/// Constraints for why: this app has confirmed precedent for ms-appx:/// XAML resource loading in
/// declarative XAML markup, but none for loading a same-assembly resource dictionary imperatively
/// from C#, so building the menu in code avoids an unverified URI-resolution question rather than
/// assuming it works the same way.
/// </summary>
public sealed class NotifyIconCoordinator : IDisposable
{
    private readonly TaskbarIcon _view;
    private readonly IDisposable _hooksLabelSubscription;

    public NotifyIconCoordinator(NotifyIconViewModel vm)
    {
        var menu = new MenuFlyout();

        var showWindowItem = new MenuFlyoutItem { Text = "Show Window" };
        BindingOperations.SetBinding(showWindowItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ShowWindowCommand)),
        });
        menu.Items.Add(showWindowItem);

        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        BindingOperations.SetBinding(settingsItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ShowSettingsCommand)),
        });
        menu.Items.Add(settingsItem);

        // Plain MenuFlyoutItem, not ToggleMenuFlyoutItem: H.NotifyIcon.WinUI's native PopupMenu
        // rendering mode never routes clicks back for a ToggleMenuFlyoutItem (confirmed: every
        // plain MenuFlyoutItem in this menu works, this was the one exception) -- root cause not
        // pinned (decompiling H.NotifyIcon.WinUI's PopulateMenu/PopupMenu found no defect in the
        // native Win32 menu construction, click-to-ID routing, or ToggleMenuFlyoutItem's cast to
        // MenuFlyoutItem, all of which checked out fine), and this app has no way to click a native
        // tray context menu to test further live. Sidesteps the whole code path instead of
        // continuing to guess at it: the label itself carries the state, so no IsChecked binding is
        // needed at all.
        var enableHooksItem = new MenuFlyoutItem { Text = "Pause Hooks" };
        BindingOperations.SetBinding(enableHooksItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.PauseHooksCommand)),
        });
        _hooksLabelSubscription = vm.WhenAnyValue(x => x.AreHooksActive)
            .Subscribe(areActive => enableHooksItem.Text = areActive ? "Pause Hooks" : "Resume Hooks");
        menu.Items.Add(enableHooksItem);

        var resumeAllItem = new MenuFlyoutItem { Text = "Resume all suspended" };
        BindingOperations.SetBinding(resumeAllItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ResumeAllSuspendedCommand)),
        });
        menu.Items.Add(resumeAllItem);

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        BindingOperations.SetBinding(exitItem, MenuFlyoutItem.CommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ExitApplicationCommand)),
        });
        menu.Items.Add(exitItem);

        foreach (var item in menu.Items)
        {
            ((FrameworkElement)item).DataContext = vm;
        }

        var view = new TaskbarIcon
        {
            DataContext = vm,
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/logo.ico")),
            ToolTipText = "WinTabber",
            ContextFlyout = menu,
            NoLeftClickDelay = true,
        };
        BindingOperations.SetBinding(view, TaskbarIcon.LeftClickCommandProperty, new Binding
        {
            Path = new PropertyPath(nameof(NotifyIconViewModel.ShowWindowCommand)),
        });
        view.ForceCreate();

        _view = view;
    }

    public void Dispose()
    {
        _hooksLabelSubscription.Dispose();
        _view?.Dispose();
    }
}
