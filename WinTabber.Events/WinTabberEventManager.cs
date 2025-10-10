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
using WinTabber.Interop;

namespace WinTabber.Events
{
    public class WinTabberEventManager : IDisposable
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
        public static WinTabberEventManager Create()
        {
            return new WinTabberEventManager().Init();
        }

        public record MouseShortcut(MouseButtons mouseButton, bool alt, bool ctrl, bool shift, bool windows);
        private List<IDisposable> _resources = new();
        private readonly HotKey _hkNextWindow = new HotKey(0, Modifiers.Alt, VirtualKeyCode.VK_OEM_3);
        private readonly HotKey _hkPrevWindow = new HotKey(1, Modifiers.Alt | Modifiers.Shift, VirtualKeyCode.VK_OEM_3);
        private readonly MouseShortcut _hkMinPlain = new MouseShortcut(MouseButtons.Left, true, true, false, false);
        private readonly MouseShortcut _hkMaxPlain = new MouseShortcut(MouseButtons.Right, true, true, false, false);
        private readonly MouseShortcut _hkMin = new MouseShortcut(MouseButtons.XButton2, false, true, false, false);
        private readonly MouseShortcut _hkMax = new MouseShortcut(MouseButtons.XButton1, false, true, false, false);
        private readonly ISubject<EventType> _eventSubject = new Subject<EventType>();

        internal WinTabberEventManager Init()
        {
            var hotKeyManager = new HotKeyManager();
            var nextWindowReg = hotKeyManager.Register(_hkNextWindow.Key, _hkNextWindow.Modifiers);
            var prevWindowReg = hotKeyManager.Register(_hkPrevWindow.Key, _hkPrevWindow.Modifiers);
            var keyHook = Hook.GlobalEvents();
            var mouseHook = WindowsInput.Capture.Global.Mouse();
            _resources.Add(hotKeyManager);
            _resources.Add(nextWindowReg);
            _resources.Add(prevWindowReg);
            _resources.Add(keyHook);
            _resources.Add(mouseHook);

            //hotKeyManager.HotKeyPressed.Subscribe(OnHotKeyPressed);
            //keyHook.KeyUp
            //keyHook.MouseDown += KeyHook_MouseDown;
            var hk = hotKeyManager.HotKeyPressed.Select(MapHotKeyToEvent);

            hotKeyManager.HotKeyPressed.Subscribe((x) =>
            {
                Debug.WriteLine("raw hk");
            });
            hk.Subscribe((x) =>
            {
                Debug.WriteLine(x);
            });


            var windowChangeSubject = new Subject<WinTabberEvent>();

            var hookHandle = _interop.HookForegroundChangeEvent();
            
            
            Events = Observable.Merge(
                hk,
                ObserveKeyHook(keyHook),
                ObserveMouseHook(keyHook),
                hookHandle.Events.Select(evt => new WinTabberEvent(EventType.ForegroundChanged, evt.Item1))
            );


            return this;
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

        public IObservable<WinTabberEvent> Events { get; private set; }


        private WinTabberEvent MapHotKeyToEvent(HotKey e)
        {
            if (e.Equals(_hkNextWindow))
            {
                return EventType.NextWindow;
            }
            else if (e.Equals(_hkPrevWindow))
            {
                return EventType.PreviousWindow;
            }

            return new WinTabberEvent(0);
        }

        public void Dispose()
        {
            _counter--;
            _resources.ForEach(r => r.Dispose());
        }
    }
}
