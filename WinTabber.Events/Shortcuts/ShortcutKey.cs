namespace WinTabber.Events.Shortcuts;

/// <summary>
/// Side-agnostic, hardware-independent key identity, backed by a Win32 virtual-key code so it
/// round-trips to both GlobalHotKeys' <c>VirtualKeyCode</c> and SharpHook's <c>KeyCode</c>.
/// Deliberately free of any WPF or SharpHook dependency — conversions live in adapter classes.
/// </summary>
public readonly record struct ShortcutKey(ushort VirtualKey)
{
    public static readonly ShortcutKey None = new(0);

    /// <summary>
    /// True for VK_CONTROL / VK_MENU / VK_SHIFT / VK_LWIN / VK_RWIN and their sided variants.
    /// Modifier keys are never valid as the "key" part of a <see cref="ShortcutTrigger.Keyboard" />.
    /// </summary>
    public bool IsModifier =>
        VirtualKey
            is VirtualKeys.Shift
                or VirtualKeys.Control
                or VirtualKeys.Menu
                or VirtualKeys.LShift
                or VirtualKeys.RShift
                or VirtualKeys.LControl
                or VirtualKeys.RControl
                or VirtualKeys.LMenu
                or VirtualKeys.RMenu
                or VirtualKeys.LWin
                or VirtualKeys.RWin;

    public bool IsNone => VirtualKey == 0;

    public override string ToString() => ShortcutDisplayNames.GetCanonicalName(this) ?? $"VK{VirtualKey:X2}";
}

/// <summary>Raw Win32 virtual-key constants used by the shortcut model.</summary>
public static class VirtualKeys
{
    public const ushort Back = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Return = 0x0D;
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Menu = 0x12;
    public const ushort Pause = 0x13;
    public const ushort Capital = 0x14;
    public const ushort Escape = 0x1B;
    public const ushort Space = 0x20;
    public const ushort PageUp = 0x21;
    public const ushort PageDown = 0x22;
    public const ushort End = 0x23;
    public const ushort Home = 0x24;
    public const ushort Left = 0x25;
    public const ushort Up = 0x26;
    public const ushort Right = 0x27;
    public const ushort Down = 0x28;
    public const ushort PrintScreen = 0x2C;
    public const ushort Insert = 0x2D;
    public const ushort Delete = 0x2E;
    public const ushort LWin = 0x5B;
    public const ushort RWin = 0x5C;
    public const ushort Apps = 0x5D;
    public const ushort Multiply = 0x6A;
    public const ushort Add = 0x6B;
    public const ushort Subtract = 0x6D;
    public const ushort Decimal = 0x6E;
    public const ushort Divide = 0x6F;
    public const ushort NumLock = 0x90;
    public const ushort Scroll = 0x91;
    public const ushort LShift = 0xA0;
    public const ushort RShift = 0xA1;
    public const ushort LControl = 0xA2;
    public const ushort RControl = 0xA3;
    public const ushort LMenu = 0xA4;
    public const ushort RMenu = 0xA5;
    public const ushort OemSemicolon = 0xBA;
    public const ushort OemPlus = 0xBB;
    public const ushort OemComma = 0xBC;
    public const ushort OemMinus = 0xBD;
    public const ushort OemPeriod = 0xBE;
    public const ushort OemQuestion = 0xBF;
    public const ushort OemTilde = 0xC0;
    public const ushort OemOpenBrackets = 0xDB;
    public const ushort OemPipe = 0xDC;
    public const ushort OemCloseBrackets = 0xDD;
    public const ushort OemQuotes = 0xDE;
}
