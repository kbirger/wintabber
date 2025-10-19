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
using WinTabber.GameBar;
using WinTabber.Interop;

namespace WinTabber.Events;

public class WinTabberEventManager : IDisposable, IWinTabberEventManager
{
    private static int _counter = 0;
    private IInteropProxy _interop;
    private WinTabberEventManager()
    {
        _interop = new InteropProxy();
        if (_counter > 0)
        {
            throw new InvalidOperationException("Can only have one instance of EventManager");
        }
        _counter++;
    }
    private static WinTabberEventManager Create()
    {
        return new WinTabberEventManager().Init();
    }

    private static WinTabberEventManager? _instance;
    public static WinTabberEventManager Instance
    {
        get
        {
            _instance ??= Create();
            return _instance;
        }
    }

    public record MouseShortcut(MouseButtons mouseButton, bool alt, bool ctrl, bool shift, bool windows);
    private List<IDisposable> _resources = new();
    private readonly IRegistration _hkNextWindow;
    private readonly IRegistration _hkPrevWindow;
    private readonly IRegistration _hkDockWindow;
    private readonly IRegistration _hkMediaWindow;
    private readonly MouseShortcut _hkMinPlain = new MouseShortcut(MouseButtons.Left, true, true, false, false);
    private readonly MouseShortcut _hkMaxPlain = new MouseShortcut(MouseButtons.Right, true, true, false, false);
    private readonly MouseShortcut _hkMin = new MouseShortcut(MouseButtons.XButton2, false, true, false, false);
    private readonly MouseShortcut _hkMax = new MouseShortcut(MouseButtons.XButton1, false, true, false, false);
    private readonly Subject<WinTabberEvent> _subject = new Subject<WinTabberEvent>();
    private Dictionary<int, EventType> _mappings = new Dictionary<int, EventType>();
    internal WinTabberEventManager Init()
    {
        var hotKeyManager = new HotKeyManager();
        var _hkNextWindow = hotKeyManager.Register(VirtualKeyCode.VK_OEM_3, Modifiers.Alt);
        var _hkPrevWindow = hotKeyManager.Register(VirtualKeyCode.VK_OEM_3, Modifiers.Alt | Modifiers.Shift);
        var _hkMediaWindow = hotKeyManager.Register(VirtualKeyCode.KEY_G, Modifiers.Alt | Modifiers.Control);

        //var _hkDockWindow = hotKeyManager.Register(Modifiers.Alt | Modifiers.Control, VirtualKeyCode.VK_LEFT);
        var keyHook = Hook.GlobalEvents();
        //var otherKeys = new KeyChordEventSource(keyHook, new ChordClick(KeyCode.LWin, KeyCode.LControl, KeyCode.Left));
        _mappings = new()
        {
            {_hkNextWindow.Id, EventType.NextWindow },
            {_hkPrevWindow.Id, EventType.PreviousWindow },
            //{_hkDockWindow.Id, EventType.NextWindow },
            {_hkMediaWindow.Id, EventType.MediaWindow },
        };
        var mouseHook = WindowsInput.Capture.Global.Mouse();
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
            ObserveHotkeys(hotKeyManager),
            ObserveKeyHook(keyHook),
            ObserveMouseHook(keyHook)
        );

        WindowChange = ObserveActiveWindowChange();
        ApplicationChange = ObserveActievApplicationChange();
        GameBarVisibilityChange = ObserveGameBar();
        return this;
    }

    private IObservable<WinTabberEvent<string>> ObserveActievApplicationChange()
    {
        return WindowChange.Select(evt => _interop.GetWindowProcessId((int)evt.Arg.Handle))
        .DistinctUntilChanged()
        .Select(pid => new WinTabberEvent<string>(EventType.ActiveApplicatonChanged, Process.GetProcessById(pid).ProcessName));
    }

    private IObservable<WinTabberEvent> ObserveHotkeys(HotKeyManager hotKeyManager)
    {
        return hotKeyManager.HotKeyPressed.Select(MapHotKeyToEvent);
    }

    private IObservable<WinTabberEvent<bool>> ObserveGameBar()
    {
        return GameBarDetector.GameBarVisibility.Select(visible => new WinTabberEvent<bool>(EventType.GameBar, visible));
    }

    private IObservable<WinTabberEvent<ActiveWindowChangeData>> ObserveActiveWindowChange()
    {
        return _interop.ActiveWindowChangedEvents()
            .DistinctUntilChanged(evt => evt.Handle)
            .Where(evt => evt.Handle != 0)
            .Select(evt => new WinTabberEvent<ActiveWindowChangeData>(EventType.ActiveWindowChanged, evt));
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
                    handler(EventType.MinimizeWindow);
                }
                else if (pressed.Equals(_hkMaxPlain) || pressed.Equals(_hkMax))
                {
                    handler(EventType.MaximizeWindow);

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
                    handler(EventType.AppHide);
                }
            };


            return rawHandler;
        },
        handler => keyHook.KeyUp += handler,
        handler => keyHook.KeyUp -= handler);
    }

    public IObservable<WinTabberEvent> CommandEvents { get; private set; }
    public IObservable<WinTabberEvent<ActiveWindowChangeData>> WindowChange { get; private set; }
    public IObservable<WinTabberEvent<string>> ApplicationChange { get; private set; }
    public IObservable<WinTabberEvent<bool>> GameBarVisibilityChange { get; private set; }

    private WinTabberEvent MapHotKeyToEvent(HotKey e)
    {
        return _mappings.TryGetValue(e.Id, out var eventType) ? eventType : 0;
    }

    public void Dispose()
    {
        _counter--;
        _resources.ForEach(r => r.Dispose());
    }
}
