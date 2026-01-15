using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinTabber.Interop;

namespace WinTabber.Events;

public class HyperKeyState(Keys hyperKey, IObservable<HyperKeyState.KeyEvent> keyEvents, IInteropProxy interop)
{
    public const int TapDelayMs = 200;
    private readonly Keys _hyperKey = hyperKey;
    private readonly IObservable<KeyEvent> _keyEvents = keyEvents;
    private readonly IInteropProxy _interop = interop;

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

    private void SendTap(Keys key)
    {
        _interop.SendInput((ushort)key, true);
        _interop.SendInput((ushort)key, false);
    }
    public IObservable<HyperKeyAction> Connect()
    {

        return Observable.Create<HyperKeyAction>((obs) =>
        {
            //bool pause = false;
            Keys lastKey = 0;
            long start = 0;
            var sub = _keyEvents.Subscribe(e =>
            {
                //if(pause)
                {
                    //return;
                }
                var now = Environment.TickCount64;
                if (e.IsDown)
                {
                    lastKey = e.Event.KeyCode;

                    if (e.Event.KeyCode == _hyperKey)
                    {
                        start = now;
                        e.Event.SuppressKeyPress = true;
                        e.Event.Handled = true;
                        SendModifiers(true);
                    }
                }
                else
                {
                    if (e.Event.KeyCode == _hyperKey)
                    {
                        SendModifiers(false);
                        var holdDuration = now - start;
                        if (holdDuration < TapDelayMs && holdDuration > 20)
                        {
                            //pause = true;
                            SendTap(_hyperKey);
                            //pause = false;
                        }
                    }
                }
            });


            return sub;

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
