using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using WinTabber.Interop;

namespace WinTabber.Events
{
    public class WinTabberEventManagerThreadHost : IWinTabberEventManager
    {
        public WinTabberEventManagerThreadHost(WinTabberEventManager eventManager)
        {
            _manager = eventManager;
            var scheduler = GetScheduler();
            CommandEvents = _manager.CommandEvents
                .Publish()
                .RefCount()
                .SubscribeOn(scheduler);

            WindowChange = _manager.WindowChange
                .SubscribeOn(scheduler);
            ApplicationChange = _manager.ApplicationChange
                .SubscribeOn(scheduler);
        }



        private readonly WinTabberEventManager _manager;

        private EventLoopScheduler GetScheduler()
        {
            return  new EventLoopScheduler(ts =>
            {
                var thread = new Thread(() =>
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                    ts();
                    Application.Run();
                })
                {
                    IsBackground = true
                };
                thread.SetApartmentState(ApartmentState.STA);
                return thread;
            });
        }

        public void SendEvent(WinTabberEvent evt)
        {
            _manager?.SendEvent(evt);
        }

        public void Dispose()
        {
            _manager?.Dispose();
        }

        public IObservable<WinTabberEvent> CommandEvents { get; private set; }

        public IObservable<WinTabberEvent<string>> ApplicationChange { get; private set; }


        public IObservable<WinTabberEvent<int>> WindowChange { get; private set; }

    }
}
