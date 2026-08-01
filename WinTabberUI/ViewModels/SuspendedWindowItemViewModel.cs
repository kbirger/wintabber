using ReactiveUI;
using System.Reactive;
using WinTabber.API.Suspension;
using WinTabber.Events;

namespace WinTabberUI.ViewModels;

public class SuspendedWindowItemViewModel : ReactiveObject
{
    public SuspendedWindowItemViewModel(SuspendedWindowEntry entry, IProcessSuspensionService suspensionService, WinTabberEventManager eventManager)
    {
        ProcessId = entry.ProcessId;
        ProcessName = entry.ProcessName;
        Title = entry.Title;

        ResumeCommand = ReactiveCommand.Create(() =>
        {
            suspensionService.Resume(ProcessId);
            // Same event WindowSelectorViewModel.SelectAndClose() sends: it maps to IsSwitcherActiveChanges
            // going false, which hides the selector AND collapses this bar's own visibility observable.
            eventManager.SendEvent(EventType.WindowSelected);
        });
    }

    public int ProcessId { get; }
    public string ProcessName { get; }
    public string Title { get; }

    public ReactiveCommand<Unit, Unit> ResumeCommand { get; }
}
