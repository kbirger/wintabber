namespace WinTabber.Events.Shortcuts;

/// <summary>
/// Which command fired, and <b>which of its triggers</b> fired. The trigger is required, not
/// informational: <see cref="SwitcherCommitTracker" /> captures its modifier set to decide when the
/// switcher commits (§5).
/// </summary>
public readonly record struct ShortcutActivation(ShortcutCommand Command, ShortcutTrigger Trigger);
