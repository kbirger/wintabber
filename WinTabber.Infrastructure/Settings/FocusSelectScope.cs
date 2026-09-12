namespace WinTabberUI.Services;

public enum FocusSelectScope
{
    /// <summary>Minimize the other windows the switcher session was showing for this application.</summary>
    SwitcherWindows = 0,

    /// <summary>Minimize every other window system-wide (via the OS's own "minimize all but active" gesture).</summary>
    AllWindows = 1,
}
