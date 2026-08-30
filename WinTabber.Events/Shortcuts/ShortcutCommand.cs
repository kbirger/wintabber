namespace WinTabber.Events.Shortcuts;

/// <summary>
/// Bindable commands only (decision D5). <see cref="EventType" /> mixes commands with
/// notifications (<c>ActiveWindowChanged</c>, <c>WindowSelected</c>, …) which are not bindable, so
/// the settings UI enumerates this enum and never <see cref="EventType" />.
/// <para>
/// Persisted as the stable strings from <see cref="ShortcutCommandExtensions.ToPersistedId" />
/// (decision D4), never as ordinals — inserting a member must not remap saved bindings.
/// </para>
/// </summary>
public enum ShortcutCommand
{
    NextWindow,
    PreviousWindow,
    CommitSelection,
    DockWindow,
    MinimizeWindow,
    MaximizeWindow,
    MediaWindow,
    ShowSettings,
    ThumbnailWindow,
    SuspendedWindows,
    SuspendWindow,
}

public static class ShortcutCommandExtensions
{
    /// <summary>The single <see cref="ShortcutCommand" /> -&gt; <see cref="EventType" /> mapping.</summary>
    public static EventType ToEventType(this ShortcutCommand command) =>
        command switch
        {
            ShortcutCommand.NextWindow => EventType.CmdNextWindow,
            ShortcutCommand.PreviousWindow => EventType.CmdPreviousWindow,
            ShortcutCommand.CommitSelection => EventType.CmdCommitSelection,
            ShortcutCommand.DockWindow => EventType.CmdDockWindow,
            ShortcutCommand.MinimizeWindow => EventType.CmdMinimizeWindow,
            ShortcutCommand.MaximizeWindow => EventType.CmdMaximizeWindow,
            ShortcutCommand.MediaWindow => EventType.CmdMediaWindow,
            ShortcutCommand.ShowSettings => EventType.CmdShowSettings,
            ShortcutCommand.ThumbnailWindow => EventType.CmdThumbnailWindow,
            ShortcutCommand.SuspendedWindows => EventType.CmdSuspendedWindows,
            ShortcutCommand.SuspendWindow => EventType.CmdSuspendWindow,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unmapped shortcut command."),
        };

    /// <summary>
    /// Stable persistence id (D4). Currently identical to the member name, but kept as an explicit
    /// switch so a future rename of the enum member cannot silently invalidate saved settings.
    /// </summary>
    public static string ToPersistedId(this ShortcutCommand command) =>
        command switch
        {
            ShortcutCommand.NextWindow => "NextWindow",
            ShortcutCommand.PreviousWindow => "PreviousWindow",
            ShortcutCommand.CommitSelection => "CommitSelection",
            ShortcutCommand.DockWindow => "DockWindow",
            ShortcutCommand.MinimizeWindow => "MinimizeWindow",
            ShortcutCommand.MaximizeWindow => "MaximizeWindow",
            ShortcutCommand.MediaWindow => "MediaWindow",
            ShortcutCommand.ShowSettings => "ShowSettings",
            ShortcutCommand.ThumbnailWindow => "ThumbnailWindow",
            ShortcutCommand.SuspendedWindows => "SuspendedWindows",
            ShortcutCommand.SuspendWindow => "SuspendWindow",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unmapped shortcut command."),
        };

    /// <summary>Inverse of <see cref="ToPersistedId" />. Unknown ids are ignored, not fatal (§6.3).</summary>
    public static bool TryParsePersistedId(string? id, out ShortcutCommand command)
    {
        foreach (var candidate in Enum.GetValues<ShortcutCommand>())
        {
            if (string.Equals(candidate.ToPersistedId(), id, StringComparison.OrdinalIgnoreCase))
            {
                command = candidate;
                return true;
            }
        }

        command = default;
        return false;
    }

    /// <summary>
    /// Commands the user may bind in the settings UI. <see cref="ShortcutCommand.CommitSelection" />
    /// is excluded: it is derived per-activation (§5), never bound.
    /// </summary>
    public static IReadOnlyList<ShortcutCommand> Bindable { get; } =
        Enum.GetValues<ShortcutCommand>().Where(c => c != ShortcutCommand.CommitSelection).ToArray();

    /// <summary>Commands that open the window switcher, and therefore capture a hold set (§5).</summary>
    public static bool OpensSwitcher(this ShortcutCommand command) =>
        command is ShortcutCommand.NextWindow or ShortcutCommand.PreviousWindow;
}
