using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabber.Events;

namespace WinTabberUI.Services;
public partial class MediaControlsStateService(WinTabberEventManager eventManager) : IMediaControlsStateService
{
    private readonly WinTabberEventManager _eventManager = eventManager;

    [Lazy]
    private IObservable<bool> GetIsMediaControlsVisibleChanges()
    {
        return _eventManager.CommandEvents
            .SubscribeOn(RxApp.TaskpoolScheduler)
            .Where(evt => evt.Type == EventType.CmdMediaWindow)
            .Scan(false, (current, _) => !current)
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();
    }
}
