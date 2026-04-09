using ReactiveUI;
using WinTabber.API;




namespace WinTabberUI.ViewModels;

public class ApplicationStateViewModel : ReactiveObject
{
    public required IObservable<WindowRef?> ActiveWindowChanges { get; init; }
    public required IObservable<ApplicationRef?> ActiveApplicationChanges { get; init; }
    //public required IObservable<bool> IsSwitcherActiveChanges { get; init; }
    public required IObservable<bool> IsDockActiveChanges { get; init; }
    public required IObservable<bool> IsMediaControlsActiveChanges { get; init; }
}
