using SharpHook;
using SharpHook.Data;
using System.Reactive.Linq;
using System.Windows.Forms;
using WinTabber.Events.Shortcuts.Detection;
using WinTabber.Interop;

namespace WinTabber.Events;

public class HyperKeyState(
    KeyCode hyperKey,
    IObservable<KeyboardHookEventArgs> keyDownEvents,
    IObservable<KeyboardHookEventArgs> keyUpEvents,
    IInteropProxy interop,
    IInputCaptureGate? captureGate = null)
{
    public const int TapDelayMs = 200;
    private readonly KeyCode _hyperKey = hyperKey;
    private readonly IObservable<KeyboardHookEventArgs> _keyDownEvents = keyDownEvents;
    private readonly IObservable<KeyboardHookEventArgs> _keyUpEvents = keyUpEvents;
    private readonly IInteropProxy _interop = interop;

    /// <summary>
    /// While a shortcut capture session is open the hyperkey steps aside entirely (§3.4): it does
    /// not suppress CapsLock and does not inject its four modifiers. Without this bypass, pressing
    /// CapsLock during capture would be recorded as Ctrl+Alt+Shift+Win instead of as CapsLock.
    /// </summary>
    private readonly IInputCaptureGate? _captureGate = captureGate;

    private bool IsBypassed => _captureGate?.IsCapturing == true;

    public enum HyperKeyAction
    {
        Tap,
        ChordStart,
        ChordEnd
    }
    public record struct KeyEvent
    {
        private KeyEventArgs _evt;
        private readonly bool _isDown;
        private long _tickCount64;

        public KeyEvent(KeyEventArgs evt, bool isDown, long tickCount64)
        {
            _evt = evt;
            _isDown = isDown;
            _tickCount64 = tickCount64;
        }

        public long Timestamp => _tickCount64;

        public KeyEventArgs Event => _evt;

        public bool IsDown => _isDown;
    }

    public record struct HyperKeyEvent(long DownTimestamp, long UpTimestamp, bool IsTap, bool Interfered);

    public record struct HyperKeyCycle(KeyEvent Down, KeyEvent Up);

    public static KeyEvent FromEvent(KeyEventArgs evt, bool down) => new KeyEvent(evt, down, Environment.TickCount64);

    private void SendModifiers(bool down)
    {
        _interop.SendInput((ushort)Keys.ControlKey, down);
        _interop.SendInput((ushort)Keys.ShiftKey, down);
        _interop.SendInput((ushort)Keys.Menu, down);     // Alt
        _interop.SendInput((ushort)Keys.LWin, down);     // Win
    }

    private void SendTap(KeyCode key)
    {
        _interop.SendInput((ushort)key, true);
        _interop.SendInput((ushort)key, false);
    }
    public IObservable<HyperKeyAction> Connect()
    {

        return Observable.Create<HyperKeyAction>((obs) =>
        {

            //_keyDownEvents.Where(e => e.IsEventSimulated)
            //    .Subscribe(e =>
            //    {
            //        Debug.WriteLine($"{e.Data.KeyCode} DOWN");
            //    });

            //_keyUpEvents.Where(e => e.IsEventSimulated)
            //    .Subscribe(e =>
            //    {
            //        Debug.WriteLine($"{e.Data.KeyCode} UP");
            //    });
            //bool pause = false;
            KeyCode lastKey = 0;
            DateTimeOffset start = DateTimeOffset.MinValue;
            var sub = _keyDownEvents
                .Where(e => !e.IsEventSimulated && !IsBypassed)
                .Subscribe(e =>
            {
                var now = e.EventTime;
                if (e.Data.KeyCode == _hyperKey)
                {

                    e.SuppressEvent = true;
                    SendModifiers(true);

                    if (lastKey != _hyperKey)
                    {
                        start = now;
                    }
                }
                lastKey = e.RawEvent.Keyboard.KeyCode;
            });


            var sub2 = _keyUpEvents
                .Where(e => !e.IsEventSimulated && !IsBypassed)
                .Subscribe(e =>
                {
                    if (e.Data.KeyCode == _hyperKey)
                    {
                        var now = e.EventTime;

                        SendModifiers(false);
                        var holdDuration = now - start;
                        //Debug.WriteLine($"CAPS DURATION {holdDuration}");
                        if (lastKey == _hyperKey && holdDuration.TotalMilliseconds < TapDelayMs)
                        {
                            //pause = true;
                            SendTap(_hyperKey);
                            //pause = false;
                        }

                        lastKey = 0;
                    }
                });


            // Previously only `sub` was returned, leaking the key-up subscription. That now matters:
            // the hook connection is switched on Pause()/Start(), so a leaked handler would
            // accumulate one live subscription per resume.
            return new System.Reactive.Disposables.CompositeDisposable(sub, sub2);

        });
        //var hyperKeyEvents = _keyEvents.Where(e => e.Event.KeyCode == _hyperKey);

        //var otherKeyEvents = _keyEvents.Where(e => e.Event.KeyCode != _hyperKey);

        //hyperKeyEvents.Subscribe(e =>
        //{
        //    if(e.IsDown)
        //    {
        //        e.Event.SuppressKeyPress = true;
        //        e.Event.Handled = true;

        //    }
        //});

        //var hyperKeyCycles =
        //    from down in hyperKeyEvents.Where(e => e.IsDown)
        //    from up in hyperKeyEvents.Where(e => !e.IsDown).Take(1)
        //    select new HyperKeyCycle(down, up);

        //var hyperKeyActions =
        //    from cycle in hyperKeyCycles
        //    from interfered in otherKeyEvents
        //        .Where(e => e.Timestamp >= cycle.Down.Timestamp && e.Timestamp <= cycle.Up.Timestamp)
        //        .Take(1)
        //        .DefaultIfEmpty(new KeyEvent(null, false, 0))
        //        .Where(key => key.Timestamp > cycle.Down.Timestamp && key.Timestamp < cycle.Up.Timestamp)
        //        .Select(_ => true)
        //        .StartWith(false)
        //        .Take(1)
        //    select new HyperKeyEvent(cycle.Down.Timestamp, cycle.Up.Timestamp, cycle.Up.Timestamp - cycle.Down.Timestamp < TapDelayMs, interfered);

        //return Observable.Merge(
        //    hyperKeyActions.Where(evt => evt.IsTap && !evt.Interfered).Select(_ => HyperKeyAction.Tap),
        //    hyperKeyEvents.Where(evt => evt evt.IsDown).Select(_ => HyperKeyAction.ChordStart),
        //    hyperKeyEvents.Where(evt => !evt.IsDown).Select(_ => HyperKeyAction.ChordEnd)
        //);
    }
}
