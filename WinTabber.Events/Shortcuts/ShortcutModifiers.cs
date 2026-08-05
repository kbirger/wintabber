namespace WinTabber.Events.Shortcuts;

/// <summary>
/// Side-agnostic modifier set (decision D2). Left and right variants of a modifier are treated as
/// the same modifier, matching <c>RegisterHotKey</c> semantics.
/// </summary>
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}
