namespace WinTabber.Events.Shortcuts.Detection;

public enum CapturedInputKind
{
    ModifierDown,
    ModifierUp,
    KeyDown,
    KeyUp,
    MouseDown,
}

/// <summary>
/// A raw input event forwarded to an open capture session. Deliberately pre-digested into model
/// types so <c>ShortcutCaptureBox</c> never touches SharpHook.
/// </summary>
public readonly record struct CapturedInput(
    CapturedInputKind Kind,
    ShortcutModifiers Modifiers,
    ShortcutKey Key,
    ShortcutMouseButton Button
)
{
    /// <summary>The modifier this event was for, or None if it was an ordinary key/button.</summary>
    public ShortcutModifiers ModifierBit { get; init; }
}

/// <summary>Where the hook matcher forwards raw input while a capture session is open.</summary>
public interface IShortcutCaptureSink
{
    bool IsCapturing { get; }

    void Push(CapturedInput input);
}

/// <summary>
/// Read-only view of the capture gate, for components that must step aside while the user is
/// picking a shortcut. <c>HyperKeyState</c> consumes this so CapsLock captures as CapsLock rather
/// than as its Ctrl+Alt+Shift+Win expansion (§3.4).
/// </summary>
public interface IInputCaptureGate
{
    bool IsCapturing { get; }
}
