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
        //public required IObservable<Combination> KeyChordEvents { get; init; }

        public required IObservable<MouseHookEventArgs> MouseChords { get; init; }

        public void Dispose()
        {
            _disposable.Dispose();
        }
    }

    public class InputListenerOptions
    {
        //public required IReadOnlyList<Combination> KeyChords { get; init; }
    }
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

        });//.SubscribeOn(scheduler).ObserveOn(scheduler);
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


    //private static IObservable<IKeyboardMouseEvents> GetHook()
    //{
    //    return Observable.Using(
    //        () => Hook.GlobalEvents(),
    //        (hook) => Observable.Return(hook))
    //        .Publish()
    //        .RefCount();
    //}

    //private IObservable<MouseShortcut> ObserveMouseShortcuts(IKeyboardMouseEvents keyHook)
    //{
    //    return Observable.FromEvent<System.Windows.Forms.MouseEventHandler, MouseShortcut>(handler =>
    //    {
    //        System.Windows.Forms.MouseEventHandler rawHandler = (sender, e) =>
    //        {
    //            var pressed = new MouseShortcut(e.Button,
    //                Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
    //                Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
    //                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
    //                Keyboard.Modifiers.HasFlag(ModifierKeys.Windows));

    //            handler(pressed);
    //        };
    //        return rawHandler;
    //    },
    //    handler => keyHook.MouseDown += handler,
    //    handler => keyHook.MouseDown -= handler);
    //}

    //private IObservable<Combination> ObserveKeyChords(IKeyboardMouseEvents keyHook, IReadOnlyList<Combination> combinations)
    //{
    //    return Observable.Create<Combination>((observer) =>
    //    {
    //        var subscriptions = from combination in combinations
    //                            select new KeyValuePair<Combination, Action>(combination, () => observer.OnNext(combination));
    //        keyHook.OnCombination(subscriptions);

    //        return Disposable.Empty;
    //    });
    //}

    //private static IObservable<System.Windows.Forms.KeyEventArgs> ObserveKeyUp(IKeyboardMouseEvents keyHook)
    //{
    //    return Observable.FromEvent<System.Windows.Forms.KeyEventHandler, System.Windows.Forms.KeyEventArgs>(handler =>
    //    {
    //        System.Windows.Forms.KeyEventHandler rawHandler = (sender, e) =>
    //        {
    //            handler(e);
    //        };


    //        return rawHandler;
    //    },
    //    handler => keyHook.KeyUp += handler,
    //    handler => keyHook.KeyUp -= handler);
    //}

    //private static IObservable<System.Windows.Forms.KeyEventArgs> ObserveKeyDown(IKeyboardMouseEvents keyHook)
    //{
    //    return Observable.FromEvent<System.Windows.Forms.KeyEventHandler, System.Windows.Forms.KeyEventArgs>(handler =>
    //    {
    //        System.Windows.Forms.KeyEventHandler rawHandler = (sender, e) =>
    //        {
    //            handler(e);
    //        };


    //        return rawHandler;
    //    },
    //    handler => keyHook.KeyDown += handler,
    //    handler => keyHook.KeyDown -= handler);
    //}
}
