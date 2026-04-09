using H.NotifyIcon;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators;

public class NotifyIconCoordinator : IDisposable
{
    private readonly TaskbarIcon _view;

    public NotifyIconCoordinator(NotifyIconViewModel vm, Application currentApplication)
    {
        var resources = new ResourceDictionary()
        {
            Source = new Uri("pack://application:,,,/WinTabberUI;component/Resources/NotifyIconResources.xaml")
        };
        var leftClickBinding = new Binding(nameof(NotifyIconViewModel.ShowWindowCommand));
        var sysTrayMenu = resources["SysTrayMenu"] as ContextMenu;
        var view = new TaskbarIcon
        {
            DataContext = vm,
            IconSource = new BitmapImage(new Uri("pack://application:,,,/WinTabberUI;component/Images/logo.ico")),
            ToolTipText = "WinTabber",
            NoLeftClickDelay = true,
            ContextMenu = sysTrayMenu
        };
        BindingOperations.SetBinding(view, TaskbarIcon.LeftClickCommandProperty, leftClickBinding);
        view.ForceCreate();

        _view = view;
    }

    public void Dispose()
    {
        _view?.Dispose();
    }
}
