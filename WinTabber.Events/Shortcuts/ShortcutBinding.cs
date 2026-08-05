namespace WinTabber.Events.Shortcuts;

public sealed record ShortcutBinding(ShortcutCommand Command, ShortcutTrigger Trigger);

/// <summary>
/// Two or more commands claiming the same physical input. Conflicts are surfaced in the settings UI
/// but never block saving (§6.1).
/// </summary>
public sealed record ShortcutConflict(ShortcutTrigger Trigger, IReadOnlyList<ShortcutCommand> Commands);
