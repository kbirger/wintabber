using DynamicData;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WinTabber.API.Suspension;
using WinTabber.Events;

namespace WinTabberUI.ViewModels;

public class SuspendedWindowsViewModel : ReactiveObject, IDisposable
{
    private readonly ReadOnlyObservableCollection<SuspendedWindowItemViewModel> _items;
    private readonly CompositeDisposable _cleanUp;

    public SuspendedWindowsViewModel(IProcessSuspensionService suspensionService, WinTabberEventManager eventManager)
    {
        var subscription = suspensionService
            .Connect()
            .Transform(entry => new SuspendedWindowItemViewModel(entry, suspensionService, eventManager))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out _items)
            .Subscribe();

        _cleanUp = new CompositeDisposable(subscription);
    }

    public ReadOnlyObservableCollection<SuspendedWindowItemViewModel> Items => _items;

    public void Dispose()
    {
        _cleanUp.Dispose();
    }
}
