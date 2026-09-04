using SharpHook;
using SharpHook.Providers;
using SharpHook.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Forms;
using System.Windows.Threading;

namespace WinTabber.Events;

public class InputListenerService
{
    public class InputListenerEvents(IDisposable disposable) : IDisposable
    {
        private readonly IDisposable _disposable = disposable;

        public required IObservable<KeyboardHookEventArgs> KeyDownEvents { get; init; }
        public required IObservable<KeyboardHookEventArgs> KeyUpEvents { get; init; }

        public required IObservable<MouseHookEventArgs> MouseChords { get; init; }

        public void Dispose()
        {
            _disposable.Dispose();
        }
    }

    public class InputListenerOptions { }

    public InputListenerService()
    {

    }

    public IObservable<InputListenerEvents> GetEvents(InputListenerOptions options)
    {
        var scheduler = GetScheduler();
        return Observable.Create<InputListenerEvents>((observer) =>
        {
            UioHookProvider.Instance.KeyTypedEnabled = false;
            var hook = new ReactiveGlobalHookAdapter(new SimpleGlobalHook(
                runAsyncOnBackgroundThread: true));

            var keyUpEvents = hook.KeyReleased;
            var keyDownEvents = hook.KeyPressed;
            var mouseShortcutEvents = hook.MousePressed;

            var disposer = Disposable.Create(() => { hook.Dispose(); });
            var result = new InputListenerEvents(disposer)
            {
                KeyDownEvents = keyDownEvents,
                KeyUpEvents = keyUpEvents,
                MouseChords = mouseShortcutEvents
            };

            hook.RunAsync();
            observer.OnNext(result);

            return disposer;

        });
    }

    private static EventLoopScheduler GetScheduler()
    {
        return new EventLoopScheduler(ts =>
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
}
