using System.Windows;
using ReactiveUI;
using WinTabber.Events;
using System.Reactive;

namespace WinTabberUI;




/// <summary>
/// Provides bindable properties and commands for the NotifyIcon. In this sample, the
/// view model is assigned to the NotifyIcon in XAML. Alternatively, the startup routing
/// in App.xaml.cs could have created this view model, and assigned it to the NotifyIcon.
/// </summary>
public partial class NotifyIconViewModel : ReactiveObject
{
    public NotifyIconViewModel(WinTabberEventManager eventManager)
    {
        ExitApplicationCommand = ReactiveCommand.Create(ExitApplication);
        ShowSettingsCommand = ReactiveCommand.Create(() => { });
        ShowWindowCommand = ReactiveCommand.Create(ShowWindow(eventManager));
    }

    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitApplicationCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
    


    public Action ShowWindow(WinTabberEventManager eventManager)
    {
        return () => eventManager.SendEvent(EventType.CmdNextWindow);
    }



    /// <summary>
    /// Shuts down the application.
    /// </summary>
    public void ExitApplication()
    {
        Application.Current.Shutdown();
    }
}