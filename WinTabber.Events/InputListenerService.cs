using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using static WinTabber.Events.WinTabberEventManager;

namespace WinTabber.Events;

public class InputListenerService
{
    public class InputListenerEvents(IDisposable disposable) : IDisposable
    {
        private readonly IDisposable _disposable = disposable;

        public required IObservable<System.Windows.Forms.KeyEventArgs> KeyDownEvents { get; init; }
        public required IObservable<System.Windows.Forms.KeyEventArgs> KeyUpEvents { get; init; }
        public required IObservable<Combination> KeyChordEvents { get; init; }

        public required IObservable<MouseShortcut> MouseChords { get; init; }

        public void Dispose()
        {
            _disposable.Dispose();
        }
    }

    public class InputListenerOptions
    {
        public required IReadOnlyList<Combination> KeyChords { get; init; }
    }
    private CompositeDisposable _cleanup;

    public InputListenerService()
    {

    }

    public IObservable<InputListenerEvents> GetEvents(InputListenerOptions options)
    {
        var scheduler = GetScheduler();
        return Observable.Create<InputListenerEvents>((observer) =>
        {
            var hook = Hook.GlobalEvents();

            var keyUpEvents = ObserveKeyUp(hook).Publish().RefCount();
            var keyDownEvents = ObserveKeyDown(hook).Publish().RefCount();
            var keyChordEvents = ObserveKeyChords(hook, options.KeyChords).Publish().RefCount();
            var mouseShortcutEvents = ObserveMouseShortcuts(hook).Publish().RefCount();

            var disposer = Disposable.Create(() => { hook.Dispose(); });
            var result = new InputListenerEvents(disposer)
            {
                KeyChordEvents = keyChordEvents,
                KeyDownEvents = keyDownEvents,
                KeyUpEvents = keyUpEvents,
                MouseChords = mouseShortcutEvents
            };

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


    private static IObservable<IKeyboardMouseEvents> GetHook()
    {
        return Observable.Using(
            () => Hook.GlobalEvents(),
            (hook) => Observable.Return(hook))
            .Publish()
            .RefCount();
    }

    private IObservable<MouseShortcut> ObserveMouseShortcuts(IKeyboardMouseEvents keyHook)
    {
        return Observable.FromEvent<System.Windows.Forms.MouseEventHandler, MouseShortcut>(handler =>
        {
            System.Windows.Forms.MouseEventHandler rawHandler = (sender, e) =>
            {
                var pressed = new MouseShortcut(e.Button,
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Windows));

                handler(pressed);
            };
            return rawHandler;
        },
        handler => keyHook.MouseDown += handler,
        handler => keyHook.MouseDown -= handler);
    }

    private IObservable<Combination> ObserveKeyChords(IKeyboardMouseEvents keyHook, IReadOnlyList<Combination> combinations)
    {
        return Observable.Create<Combination>((observer) =>
        {
            var subscriptions = from combination in combinations
                                select new KeyValuePair<Combination, Action>(combination, () => observer.OnNext(combination));
            keyHook.OnCombination(subscriptions);

            return Disposable.Empty;
        });
    }

    private static IObservable<System.Windows.Forms.KeyEventArgs> ObserveKeyUp(IKeyboardMouseEvents keyHook)
    {
        return Observable.FromEvent<System.Windows.Forms.KeyEventHandler, System.Windows.Forms.KeyEventArgs>(handler =>
        {
            System.Windows.Forms.KeyEventHandler rawHandler = (sender, e) =>
            {
                handler(e);
            };


            return rawHandler;
        },
        handler => keyHook.KeyUp += handler,
        handler => keyHook.KeyUp -= handler);
    }

    private static IObservable<System.Windows.Forms.KeyEventArgs> ObserveKeyDown(IKeyboardMouseEvents keyHook)
    {
        return Observable.FromEvent<System.Windows.Forms.KeyEventHandler, System.Windows.Forms.KeyEventArgs>(handler =>
        {
            System.Windows.Forms.KeyEventHandler rawHandler = (sender, e) =>
            {
                handler(e);
            };


            return rawHandler;
        },
        handler => keyHook.KeyDown += handler,
        handler => keyHook.KeyDown -= handler);
    }
}
