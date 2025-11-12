namespace WinTabberUI.Services;

public interface IWindowSelectorStateService
{
    IObservable<bool> IsEditingChanges { get; }
    IObservable<bool> WindowSelectorChanges { get; }

}