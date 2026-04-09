using GlobalHotKeys;
using GlobalHotKeys.Native.Types;
using Gma.System.MouseKeyHook;
using SharpHook;
using SharpHook.Data;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using WinTabber.Interop;
using static WinTabber.Events.InputListenerService;

namespace WinTabber.Events;

public class WinTabberEventManager : IDisposable, IWinTabberEventManager, INotifyPropertyChanged
{
    private IInteropProxy _interop;
    private readonly InputListenerService _inputListener;

    public WinTabberEventManager(IInteropProxy interop, InputListenerService inputListener)
    {
        _interop = interop;
        _inputListener = inputListener;
        Init();
    }

    public record struct MouseShortcut(MouseButtons mouseButton, bool alt, bool ctrl, bool shift, bool windows);
    private List<IDisposable> _resources = new();
    private IRegistration? _hkNextWindow;
    private IRegistration? _hkPrevWindow;
    private IRegistration? _hkDockWindow;
    private IRegistration? _hkMediaWindow;
    private readonly MouseShortcut _hkMinPlain = new MouseShortcut(MouseButtons.Left, true, true, false, false);
    private readonly MouseShortcut _hkMaxPlain = new MouseShortcut(MouseButtons.Right, true, true, false, false);
    private readonly MouseShortcut _hkMin = new MouseShortcut(MouseButtons.XButton2, false, true, false, false);
    private readonly MouseShortcut _hkMax = new MouseShortcut(MouseButtons.XButton1, false, true, false, false);
    private readonly Subject<WinTabberEvent> _subject = new Subject<WinTabberEvent>();
    private Dictionary<int, EventType> _mappings = new Dictionary<int, EventType>();

    private BehaviorSubject<bool> _enabled = new BehaviorSubject<bool>(false);

    [MemberNotNull(nameof(CommandEvents), nameof(ApplicationChange), nameof(WindowChange))]
    internal WinTabberEventManager Init()
    {
        var scheduler = GetScheduler();
        var connection = GetConnection();
        var hooks = connection.SelectMany(events =>
        {
            return CreateHookEvents(scheduler, events);
        });
        
     
        //var hooks = connection.SelectMany(events => CreateHookEvents(scheduler, events));
        var hks = CreateHotKeyEventsObservable(scheduler);
        CommandEvents = Observable.Merge(hks, _subject, hooks).Publish().RefCount();
        WindowChange = CreateWindowChangeObservable(scheduler);
        ApplicationChange = CreateApplicaionChangeObservable(scheduler);

        
        return this;
    }

    public bool IsRunning
    {
        get => _hooksConnection != null;
        private set
        {
            if (value != IsRunning)
            {
                if (value)
                {
                    Start();
                }
                else
                {
                    Pause();
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            }
        }
    }

    private IObservable<InputListenerEvents> GetConnection()
    {
        //var scheduler = GetScheduler();

        return _enabled
            .Select(state =>
            {
                if (!state)
                {
                    return Observable.Empty<InputListenerEvents>();
                }

                return Observable.Defer(() =>
                    _inputListener.GetEvents(new()
                    {
                        //KeyChords = []
                    })).Publish().RefCount() ;
            })
            .Switch()
            .Replay(1)
            .RefCount();       
    }

    public void Pause()
    {
        _enabled.OnNext(false);        
    }

    public void Start()
    {
        _enabled.OnNext(true);
    }
    private IObservable<WinTabberEvent<string>> CreateApplicaionChangeObservable(EventLoopScheduler scheduler)
    {
        return ObserveActiveApplicationChange()
            .SubscribeOn(scheduler);
    }

    private IObservable<WinTabberEvent<int>> CreateWindowChangeObservable(EventLoopScheduler scheduler)
    {
        return ObserveActiveWindowChange()
            .SubscribeOn(scheduler);
    }


    private IObservable<WinTabberEvent> CreateHotKeyEventsObservable(EventLoopScheduler scheduler)
    {
        _hotKeyManager ??= new HotKeyManager();
        _hkNextWindow ??= _hotKeyManager.Register(VirtualKeyCode.VK_OEM_3, Modifiers.Alt);
        _hkPrevWindow ??= _hotKeyManager.Register(VirtualKeyCode.VK_OEM_3, Modifiers.Alt | Modifiers.Shift);
        _hkMediaWindow ??= _hotKeyManager.Register(VirtualKeyCode.KEY_G, Modifiers.Alt | Modifiers.Control);
        //var otherKeys = new KeyChordEventSource(keyHook, new ChordClick(KeyCode.LWin, KeyCode.LControl, KeyCode.Left));
        _mappings = new()
        {
            {_hkNextWindow.Id, EventType.CmdNextWindow },
            {_hkPrevWindow.Id, EventType.CmdPreviousWindow },
            //{_hkDockWindow.Id, EventType.NextWindow },
            {_hkMediaWindow.Id, EventType.CmdMediaWindow },
        };
        // var mouseHook = WindowsInput.Capture.Global.MouseAsync();
        _resources.Add(_hotKeyManager);
        _resources.Add(_hkNextWindow);
        _resources.Add(_hkPrevWindow);
        //_resources.Add(dockWindowReg);
        _resources.Add(_hkMediaWindow);

        return ObserveHotkeys(_hotKeyManager).SubscribeOn(scheduler);
    }
    private IObservable<WinTabberEvent> CreateHookEvents(EventLoopScheduler scheduler, InputListenerEvents events)
    {

        //var _hkDockWindow = _hotKeyManager.Register(Modifiers.Alt | Modifiers.Control, VirtualKeyCode.VK_LEFT);
        //var keyHook = Hook.GlobalEvents();

        //_resources.Add(keyHook);
        var hyperkey = new HyperKeyState(KeyCode.VcCapsLock,
            events.KeyDownEvents,
            events.KeyUpEvents, _interop);

        var capsHook = hyperkey.Connect().Subscribe();

        //.Select(action =>
        //{
        //    Debug.WriteLine(action);
        //    if (action == HyperKeyState.HyperKeyAction.Tap)
        //    {
        //        ToggleCapsLock();
        //    }
        //    else if (action == HyperKeyState.HyperKeyAction.ChordStart)
        //    {
        //        //SendModifiers(true);
        //    }
        //    else if (action == HyperKeyState.HyperKeyAction.ChordEnd)
        //    {
        //        //SendModifiers(false);
        //    }

        //    return WinTabberEvent.None;
        //});



        var hooks = Observable.Merge(
            ObserveKeyCommands(events.KeyUpEvents),
            ObserveMouseHook(events.MouseChords),
            ObserveKeyChords(events.KeyDownEvents)
        )
        .Where(evt => WinTabberEvent.None != evt)
        //.SubscribeOn(scheduler)
        .Publish()

        .RefCount();
        return hooks;
    }

    private void SendModifiers(bool down)
    {
        _interop.SendInput((ushort)Keys.ControlKey, down);
        _interop.SendInput((ushort)Keys.ShiftKey, down);
        _interop.SendInput((ushort)Keys.Menu, down);     // Alt
        _interop.SendInput((ushort)Keys.LWin, down);     // Win
    }

    private void ToggleCapsLock()
    {
        //_interop.SendInput((ushort)Keys.CapsLock, true);
        //_interop.SendInput((ushort)Keys.CapsLock, false);
    }

    private IObservable<WinTabberEvent> ObserveKeyChords(IObservable<KeyboardHookEventArgs> chords)
    {
        //var dockChord = Combination.TriggeredBy(Keys.Left).With(Keys.LWin).With(Keys.Control);

        return chords
            .Where(e => 
                !e.IsEventSimulated 
                && e.RawEvent.Mask.HasFlag(EventMask.LeftMeta) 
                && e.RawEvent.Mask.HasFlag(EventMask.LeftCtrl) 
                && e.RawEvent.Keyboard.KeyCode == KeyCode.VcLeft)
            .Select(_ => new WinTabberEvent(EventType.CmdDockWindow));        
    }


    private IObservable<WinTabberEvent<string>> ObserveActiveApplicationChange()
    {
        return WindowChange.Select(evt => _interop.GetWindowProcessId(evt.Arg))
        .DistinctUntilChanged()
        .Select(pid => Process.GetProcessById(pid).ProcessName)
        .DistinctUntilChanged()
        .Select(processName => new WinTabberEvent<string>(EventType.ActiveApplicatonChanged, processName));
    }

    private IObservable<WinTabberEvent> ObserveHotkeys(HotKeyManager hotKeyManager)
    {
        return _hotKeyManager.HotKeyPressed.Select(MapHotKeyToEvent);
    }

    private IObservable<WinTabberEvent<int>> ObserveActiveWindowChange()
    {
        return _interop.ActiveWindowChangedEvents()
            .Select(data => data.Handle)
            .StartWith(_interop.GetForegroundWindowHandle())
            .DistinctUntilChanged()
            .Where(evt => evt != 0)
            .Select(evt => new WinTabberEvent<int>(EventType.ActiveWindowChanged, evt));
    }

    public void SendEvent(WinTabberEvent evt)
    {
        _subject.OnNext(evt);
    }

    private static MouseButtons MouseButton(SharpHook.Data.MouseButton button)
    {
        return button switch
        {
            SharpHook.Data.MouseButton.Button1 => MouseButtons.Left,
            SharpHook.Data.MouseButton.Button2 => MouseButtons.Right,
            SharpHook.Data.MouseButton.Button4 => MouseButtons.XButton1,
            SharpHook.Data.MouseButton.Button5 => MouseButtons.XButton2,
            _ => MouseButtons.None  // temporary. should switch to the new interface
        };
    }
    private IObservable<WinTabberEvent> ObserveMouseHook(IObservable<MouseHookEventArgs> mouseDownEvents)
    {
        return mouseDownEvents
            .Select(e =>
            {
                var (ctrl, alt, shift, win) = GetMods(e.RawEvent);
                var pressed = new MouseShortcut(MouseButton(e.RawEvent.Mouse.Button), alt, ctrl, shift, win);

                if (pressed.Equals(_hkMinPlain) || pressed.Equals(_hkMin))
                {
                    return EventType.CmdMinimizeWindow;
                }
                else if (pressed.Equals(_hkMaxPlain) || pressed.Equals(_hkMax))
                {
                    return EventType.CmdMaximizeWindow;
                }

                return WinTabberEvent.None;
            });
    }

    private (bool ctrl, bool alt, bool shift, bool win) GetMods(UioHookEvent e)
    {
        var flags = e.Mask;
        return (
            e.Mask.HasFlag(EventMask.LeftCtrl),
            e.Mask.HasFlag(EventMask.LeftAlt),
            e.Mask.HasFlag(EventMask.LeftShift),
            e.Mask.HasFlag(EventMask.LeftMeta)
        );
    }

    private static IObservable<WinTabberEvent> ObserveKeyCommands(IObservable<KeyboardHookEventArgs> keyDownEvents)
    {
        return keyDownEvents.Where(e => !e.IsEventSimulated && e.RawEvent.Keyboard.KeyCode == KeyCode.VcLeftAlt)
            .Select(_ => new WinTabberEvent(EventType.CmdAppHide));
    }

    

    private IConnectableObservable<WinTabberEvent> _hooks;
    private IDisposable? _hooksConnection;
    private HotKeyManager _hotKeyManager;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IObservable<WinTabberEvent> CommandEvents { get; private set; }
    public IObservable<WinTabberEvent<int>> WindowChange { get; private set; }
    public IObservable<WinTabberEvent<string>> ApplicationChange { get; private set; }
    private WinTabberEvent MapHotKeyToEvent(HotKey e)
    {
        return _mappings.TryGetValue(e.Id, out var eventType) ? eventType : 0;
    }

    public void Dispose()
    {
        _resources.ForEach(r => r.Dispose());
    }

    private EventLoopScheduler GetScheduler()
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
