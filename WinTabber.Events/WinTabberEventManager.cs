using GlobalHotKeys;
using GlobalHotKeys.Native.Types;
using Gma.System.MouseKeyHook;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsInput.Events;
using WindowsInput.Events.Sources;
using WinTabber.Interop;

namespace WinTabber.Events;

public class WinTabberEventManager : IDisposable, IWinTabberEventManager
{
    private IInteropProxy _interop;
    public  WinTabberEventManager(IInteropProxy interop)
    {
        _interop = interop;
        Init();
    }


    public record MouseShortcut(MouseButtons mouseButton, bool alt, bool ctrl, bool shift, bool windows);
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
    internal WinTabberEventManager Init()
    {
        var hotKeyManager = new HotKeyManager();
        _hkNextWindow = hotKeyManager.Register(VirtualKeyCode.VK_OEM_3, Modifiers.Alt);
        _hkPrevWindow = hotKeyManager.Register(VirtualKeyCode.VK_OEM_3, Modifiers.Alt | Modifiers.Shift);
        _hkMediaWindow = hotKeyManager.Register(VirtualKeyCode.KEY_G, Modifiers.Alt | Modifiers.Control);
        
        //var _hkDockWindow = hotKeyManager.Register(Modifiers.Alt | Modifiers.Control, VirtualKeyCode.VK_LEFT);
        var keyHook = Hook.GlobalEvents();
        //var otherKeys = new KeyChordEventSource(keyHook, new ChordClick(KeyCode.LWin, KeyCode.LControl, KeyCode.Left));
        _mappings = new()
        {
            {_hkNextWindow.Id, EventType.CmdNextWindow },
            {_hkPrevWindow.Id, EventType.CmdPreviousWindow },
            //{_hkDockWindow.Id, EventType.NextWindow },
            {_hkMediaWindow.Id, EventType.CmdMediaWindow },
        };
        var mouseHook = WindowsInput.Capture.Global.MouseAsync();
        var keyHook2 = WindowsInput.Capture.Global.KeyboardAsync();
        _resources.Add(hotKeyManager);
        _resources.Add(_hkNextWindow);
        _resources.Add(_hkPrevWindow);
        //_resources.Add(dockWindowReg);
        _resources.Add(_hkMediaWindow);
        _resources.Add(keyHook);
        _resources.Add(mouseHook);



        //hotKeyManager.HotKeyPressed.Subscribe(OnHotKeyPressed);
        //keyHook.KeyUp
        //keyHook.MouseDown += KeyHook_MouseDown;

        CommandEvents = Observable.Merge(
            _subject,
            Observable.Create<WinTabberEvent>((observer) =>
            {
                Debug.WriteLine($"subscibeTEST TEST from {Thread.CurrentThread.ManagedThreadId}");
                return () => { };
            }),
            ObserveHotkeys(hotKeyManager),
            ObserveKeyHook(keyHook),
            ObserveMouseHook(keyHook),
            ObserveKeyChords(keyHook)
        );

        WindowChange = ObserveActiveWindowChange();
        ApplicationChange = ObserveActiveApplicationChange();
        return this;
    }

    private IObservable<WinTabberEvent> ObserveKeyChords(IKeyboardEventSource keyHook)
    {
        var eventSource = new KeyChordEventSource(keyHook, new ChordClick(KeyCode.Control, KeyCode.LWin, KeyCode.Left));
        eventSource.Enabled = true;
        eventSource.Triggered += EventSource_Triggered;
        return Observable.FromEventPattern<KeyChordEventArgs>(
            eventSource,
            nameof(eventSource.Triggered))
            .Select(evt => new WinTabberEvent(EventType.CmdDockWindow));
    }

    private IObservable<WinTabberEvent> ObserveKeyChords(IKeyboardMouseEvents keyHook)
    {
        return Observable.Create<WinTabberEvent>((observer) =>
        {

            keyHook.OnCombination(new KeyValuePair<Combination, Action>[]
            {
             new (Combination.TriggeredBy(Keys.Left).With(Keys.LWin).With(Keys.Control), () => { observer.OnNext(new WinTabberEvent(EventType.CmdDockWindow)); })
            } );


            return () => observer.OnCompleted();
        });
        


        //var eventSource = new KeyChordEventSource(keyHook, new ChordClick(KeyCode.Control, KeyCode.LWin, KeyCode.Left));
        //eventSource.Enabled = true;
        //eventSource.Triggered += EventSource_Triggered;
        //return Observable.FromEventPattern<KeyChordEventArgs>(
        //    eventSource,
        //    nameof(eventSource.Triggered))
        //    .Select(evt => new WinTabberEvent(EventType.DockWindow));
    }


    private void EventSource_Triggered(object? sender, KeyChordEventArgs e)
    {
        Debug.WriteLine("boom");
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
        return hotKeyManager.HotKeyPressed.Select(MapHotKeyToEvent);
    }

    private IObservable<WinTabberEvent<int>> ObserveActiveWindowChange()
    {
        return _interop.ActiveWindowChangedEvents()
            .Select(data => data.Handle)
            .StartWith(_interop.GetForegroundWindowHandle())
            .DistinctUntilChanged()
            .Where(evt => evt!= 0)
            .Select(evt => new WinTabberEvent<int>(EventType.ActiveWindowChanged, evt));
    }

    public void SendEvent(WinTabberEvent evt)
    {
        _subject.OnNext(evt);
    }

    private IObservable<WinTabberEvent> ObserveMouseHook(IKeyboardMouseEvents keyHook)
    {
        return Observable.FromEvent<System.Windows.Forms.MouseEventHandler, WinTabberEvent>(handler =>
        {
            System.Windows.Forms.MouseEventHandler rawHandler = (sender, e) =>
            {
                var pressed = new MouseShortcut(e.Button,
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Windows));

                if (pressed.Equals(_hkMinPlain) || pressed.Equals(_hkMin))
                {
                    handler(EventType.CmdMinimizeWindow);
                }
                else if (pressed.Equals(_hkMaxPlain) || pressed.Equals(_hkMax))
                {
                    handler(EventType.CmdMaximizeWindow);

                }
            };
            return rawHandler;
        },
        handler => keyHook.MouseDown += handler,
        handler => keyHook.MouseDown -= handler);
    }

    private static IObservable<WinTabberEvent> ObserveKeyHook(IKeyboardMouseEvents keyHook)
    {
        return Observable.FromEvent<System.Windows.Forms.KeyEventHandler, WinTabberEvent>(handler =>
        {
            System.Windows.Forms.KeyEventHandler rawHandler = (sender, e) =>
            {
                if (e.KeyCode == Keys.LMenu)
                {
                    handler(EventType.CmdAppHide);
                }
            };


            return rawHandler;
        },
        handler => keyHook.KeyUp += handler,
        handler => keyHook.KeyUp -= handler);
    }

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
}
