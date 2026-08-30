using System.Reactive.Disposables;
using System.Reactive.Linq;
using SharpHook;
using SharpHook.Data;
using static WinTabber.Events.InputListenerService;

namespace WinTabber.Events.Shortcuts.Detection;

/// <summary>
/// Matches hook-routed triggers against live SharpHook input. Replaces
/// <c>WinTabberEventManager.ObserveKeyChords</c>, <c>ObserveMouseHook</c> and
/// <c>ObserveKeyCommands</c>.
/// <para>
/// Everything that is not <see cref="ShortcutTrigger.IsHotKeyEligible" /> lands here: all
/// <see cref="ShortcutTrigger.KeyMouse" /> triggers, suppressing triggers such as the dock chord,
/// release-edge triggers, and any bare-modifier binding.
/// </para>
/// </summary>
public sealed class HookTriggerMatcher
{
    private readonly Func<ShortcutMap> _currentMap;
    private readonly Action<ShortcutModifiers> _onHeldModifiersChanged;
    private readonly IShortcutCaptureSink _captureSink;

    private ShortcutModifiers _held;

    public HookTriggerMatcher(
        Func<ShortcutMap> currentMap,
        Action<ShortcutModifiers> onHeldModifiersChanged,
        IShortcutCaptureSink captureSink
    )
    {
        _currentMap = currentMap;
        _onHeldModifiersChanged = onHeldModifiersChanged;
        _captureSink = captureSink;
    }

    public ShortcutModifiers HeldModifiers => _held;

    /// <summary>Wires up one live hook connection. Disposing the result detaches from it.</summary>
    public IObservable<ShortcutActivation> Connect(InputListenerEvents events)
    {
        return Observable.Create<ShortcutActivation>(observer =>
        {
            // The hook is torn down and rebuilt across Pause()/Start(), and the OS modifier state
            // may well have changed while it was down. Start from a clean slate.
            _held = ShortcutModifiers.None;
            _onHeldModifiersChanged(_held);

            var keyDown = events.KeyDownEvents.Subscribe(e => OnKeyEvent(observer, e, TriggerEdge.Press));
            var keyUp = events.KeyUpEvents.Subscribe(e => OnKeyEvent(observer, e, TriggerEdge.Release));
            var mouseDown = events.MouseChords.Subscribe(e => OnMouseEvent(observer, e));

            return new CompositeDisposable(keyDown, keyUp, mouseDown);
        });
    }

    private void OnKeyEvent(IObserver<ShortcutActivation> observer, KeyboardHookEventArgs e, TriggerEdge edge)
    {
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shortcut-capture-debug.log"),
            $"{DateTime.Now:HH:mm:ss.fff} OnKeyEvent edge={edge} key={e.RawEvent.Keyboard.KeyCode} simulated={e.IsEventSimulated} capturing={_captureSink.IsCapturing}\n"
        );
        var keyCode = e.RawEvent.Keyboard.KeyCode;
        var modifierBit = SharpHookAdapters.ToModifierBit(keyCode);

        UpdateHeldModifiers(e.RawEvent.Mask, modifierBit, edge);

        // Simulated events are the app's own SendInput calls (HyperKeyState's modifier injection).
        // They still count for held-modifier tracking above — the OS state they create is real and
        // the *next* genuine keystroke's mask will contain them, so skipping them there would make
        // _held drift. They must not, however, trigger commands or feed a capture session.
        //
        // IsEventSimulated derives from the OS-level "injected" flag (surfaced as the
        // EventMask.SimulatedEvent bit), so it is set for any SendInput-injected event regardless of
        // which library produced it — including IInteropProxy.SendInput from HyperKeyState.
        // Windows-only mechanism, which is fine here. Note the failure shape: SharpHook once had a
        // bug where this always returned true; if that ever regresses, *no* hook trigger matches.
        if (e.IsEventSimulated)
        {
            return;
        }

        var key = SharpHookAdapters.ToShortcutKey(e.RawEvent.Keyboard);

        if (_captureSink.IsCapturing)
        {
            e.SuppressEvent = true;
            _captureSink.Push(
                new CapturedInput(
                    modifierBit != ShortcutModifiers.None
                        ? (edge == TriggerEdge.Press ? CapturedInputKind.ModifierDown : CapturedInputKind.ModifierUp)
                        : (edge == TriggerEdge.Press ? CapturedInputKind.KeyDown : CapturedInputKind.KeyUp),
                    _held,
                    key,
                    ShortcutMouseButton.None
                )
                {
                    ModifierBit = modifierBit,
                }
            );
            return;
        }

        // Exclude the pressed key's own modifier bit so a bare-modifier binding (Modifiers = None,
        // Key = LeftAlt) can match. For ordinary keys this is a no-op.
        var modifiers = _held & ~modifierBit;

        foreach (var binding in _currentMap().Bindings)
        {
            if (binding.Trigger is not ShortcutTrigger.Keyboard keyboard || keyboard.IsHotKeyEligible)
            {
                continue;
            }

            if (keyboard.Edge != edge || keyboard.Key != key || keyboard.Modifiers != modifiers)
            {
                continue;
            }

            if (keyboard.Suppress)
            {
                e.SuppressEvent = true;
            }

            observer.OnNext(new ShortcutActivation(binding.Command, binding.Trigger));
        }
    }

    private void OnMouseEvent(IObserver<ShortcutActivation> observer, MouseHookEventArgs e)
    {
        UpdateHeldModifiers(e.RawEvent.Mask, ShortcutModifiers.None, TriggerEdge.Press);

        if (e.IsEventSimulated)
        {
            return;
        }

        var button = SharpHookAdapters.ToShortcutMouseButton(e.RawEvent.Mouse.Button);
        if (button == ShortcutMouseButton.None)
        {
            return;
        }

        if (_captureSink.IsCapturing)
        {
            e.SuppressEvent = true;
            _captureSink.Push(
                new CapturedInput(CapturedInputKind.MouseDown, _held, ShortcutKey.None, button)
            );
            return;
        }

        foreach (var binding in _currentMap().Bindings)
        {
            if (binding.Trigger is not ShortcutTrigger.KeyMouse mouse)
            {
                continue;
            }

            if (mouse.Button != button || mouse.Modifiers != _held)
            {
                continue;
            }

            if (mouse.Suppress)
            {
                e.SuppressEvent = true;
            }

            observer.OnNext(new ShortcutActivation(binding.Command, binding.Trigger));
        }
    }

    /// <summary>
    /// The event mask is the source of truth for which modifiers the OS believes are down, but it
    /// is ambiguous about the edge that produced the current event, so the modifier the event is
    /// *for* is set or cleared explicitly.
    /// </summary>
    private void UpdateHeldModifiers(UioHookEvent rawEvent, ShortcutModifiers modifierBit, TriggerEdge edge) =>
        UpdateHeldModifiers(rawEvent.Mask, modifierBit, edge);

    private void UpdateHeldModifiers(EventMask mask, ShortcutModifiers modifierBit, TriggerEdge edge)
    {
        var updated = SharpHookAdapters.ToShortcutModifiers(mask);

        if (modifierBit != ShortcutModifiers.None)
        {
            updated = edge == TriggerEdge.Press ? updated | modifierBit : updated & ~modifierBit;
        }

        if (updated == _held)
        {
            return;
        }

        _held = updated;
        _onHeldModifiersChanged(updated);
    }
}
