using SharpHook.Data;
using GlobalModifiers = GlobalHotKeys.Native.Types.Modifiers;
using GlobalVirtualKey = GlobalHotKeys.Native.Types.VirtualKeyCode;

namespace WinTabber.Events.Shortcuts.Detection;

/// <summary>
/// Conversions between the dependency-free shortcut model and the two input libraries. Everything
/// that knows about SharpHook or GlobalHotKeys types lives here, so
/// <c>WinTabber.Events.Shortcuts</c> itself stays referenceable from WPF assemblies.
/// </summary>
public static class SharpHookAdapters
{
    /// <summary>
    /// Side-agnostic modifier extraction (decision D2). This is the fix for the long-standing
    /// right-modifier bug: the old <c>GetMods</c> read only <c>LeftCtrl/LeftAlt/LeftShift/LeftMeta</c>,
    /// so right-hand modifiers never matched anything.
    /// <para>
    /// Note the bitwise test rather than <c>HasFlag</c>. <see cref="EventMask.Ctrl" /> is defined as
    /// <c>LeftCtrl | RightCtrl</c>, so <c>mask.HasFlag(EventMask.Ctrl)</c> would demand that
    /// <b>both</b> control keys be down.
    /// </para>
    /// <para>
    /// Only the eight modifier bits are ever read. <see cref="EventMask.SimulatedEvent" /> and
    /// <see cref="EventMask.SuppressEvent" /> live in this same mask, so an approach that treated
    /// the mask as "modifiers" wholesale would leak them into the bitmask.
    /// </para>
    /// </summary>
    public static ShortcutModifiers ToShortcutModifiers(EventMask mask)
    {
        var modifiers = ShortcutModifiers.None;

        if ((mask & EventMask.Ctrl) != 0)
        {
            modifiers |= ShortcutModifiers.Ctrl;
        }

        if ((mask & EventMask.Alt) != 0)
        {
            modifiers |= ShortcutModifiers.Alt;
        }

        if ((mask & EventMask.Shift) != 0)
        {
            modifiers |= ShortcutModifiers.Shift;
        }

        if ((mask & EventMask.Meta) != 0)
        {
            modifiers |= ShortcutModifiers.Win;
        }

        return modifiers;
    }

    /// <summary>
    /// The modifier a key <i>is</i>, or <see cref="ShortcutModifiers.None" /> for ordinary keys.
    /// Used to set/clear the bit for the key that generated the current event, since the mask alone
    /// is ambiguous about the transition that produced it.
    /// </summary>
    public static ShortcutModifiers ToModifierBit(KeyCode key) =>
        key switch
        {
            KeyCode.VcLeftControl or KeyCode.VcRightControl => ShortcutModifiers.Ctrl,
            KeyCode.VcLeftAlt or KeyCode.VcRightAlt => ShortcutModifiers.Alt,
            KeyCode.VcLeftShift or KeyCode.VcRightShift => ShortcutModifiers.Shift,
            KeyCode.VcLeftMeta or KeyCode.VcRightMeta => ShortcutModifiers.Win,
            _ => ShortcutModifiers.None,
        };

    /// <summary>
    /// On Windows libuiohook sets <c>rawcode</c> to the Win32 virtual-key code, which is exactly
    /// what <see cref="ShortcutKey" /> stores. That avoids maintaining a KeyCode-to-VK table.
    /// </summary>
    public static ShortcutKey ToShortcutKey(KeyboardEventData keyboard) => new((ushort)keyboard.RawCode);

    public static ShortcutMouseButton ToShortcutMouseButton(MouseButton button) =>
        button switch
        {
            MouseButton.Button1 => ShortcutMouseButton.Left,
            MouseButton.Button2 => ShortcutMouseButton.Right,
            MouseButton.Button3 => ShortcutMouseButton.Middle,
            MouseButton.Button4 => ShortcutMouseButton.X1,
            MouseButton.Button5 => ShortcutMouseButton.X2,
            _ => ShortcutMouseButton.None,
        };

    /// <summary>
    /// <see cref="ShortcutModifiers" /> to GlobalHotKeys' flags.
    /// <para>
    /// <c>NoRepeat</c> is deliberately never set: the existing Alt+` behavior relies on auto-repeat
    /// so holding the chord cycles through windows.
    /// </para>
    /// </summary>
    public static GlobalModifiers ToGlobalModifiers(ShortcutModifiers modifiers)
    {
        var result = default(GlobalModifiers);

        if (modifiers.HasFlag(ShortcutModifiers.Ctrl))
        {
            result |= GlobalModifiers.Control;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            result |= GlobalModifiers.Alt;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            result |= GlobalModifiers.Shift;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Win))
        {
            result |= GlobalModifiers.Win;
        }

        return result;
    }

    /// <summary><see cref="GlobalVirtualKey" /> is an enum over raw VK values, so this is a cast.</summary>
    public static GlobalVirtualKey ToGlobalVirtualKey(ShortcutKey key) => (GlobalVirtualKey)key.VirtualKey;
}
