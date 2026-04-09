using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Events;

namespace WinTabberUI.Services;
public partial class MediaControlsStateService(WinTabberEventManager eventManager) : IMediaControlsStateService
{
    private readonly WinTabberEventManager _eventManager = eventManager;
    private readonly BehaviorSubject<bool> _visibilityEvents = new BehaviorSubject<bool>(false);
    public void HideView()
    {
        _visibilityEvents.OnNext(false);
    }

    public void ToggleView()
    {
        _visibilityEvents.OnNext(!_visibilityEvents.Value);
    }


    [Lazy]
    private IObservable<bool> GetIsMediaControlsVisibleChanges()
    {
        var listener = _eventManager.CommandEvents
            .SubscribeOn(RxSchedulers.TaskpoolScheduler)
            .Where(evt => evt.Type == EventType.CmdMediaWindow)

            .Subscribe(_ => ToggleView());

        listener.DisposeWith(new CompositeDisposable(_visibilityEvents));
        return _visibilityEvents;
    }
}
