using System.Windows;
using ReactiveUI;
using WinTabber.Events;
using System.Reactive;
using System.Reactive.Linq;

namespace WinTabberUI.ViewModels;

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
        ShowSettingsCommand = ReactiveCommand.Create(ShowSettings(eventManager));
        ShowWindowCommand = ReactiveCommand.Create(ShowSelector(eventManager));
        PauseHooksCommand = ReactiveCommand.Create(() =>
        {

            if (eventManager.IsRunning)
            {
                eventManager.Pause();
            }
            else
            {
                eventManager.Start();
            }
        });

        _areHooksActive = eventManager.WhenAnyValue(em => em.IsRunning)
            .ToProperty(this, vm => vm.AreHooksActive);
    }

    private ObservableAsPropertyHelper<bool> _areHooksActive;
    public bool AreHooksActive => _areHooksActive.Value;
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitApplicationCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> PauseHooksCommand { get; }

    public Action ShowSettings(WinTabberEventManager eventManager)
    {
        return () => eventManager.SendEvent(EventType.CmdShowSettings);
    }

    public Action ShowSelector(WinTabberEventManager eventManager)
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