using System.Collections.Frozen;

namespace WinTabber.Events.Shortcuts;

/// <summary>
/// Immutable snapshot of the whole keymap. Replaced wholesale on save; never mutated in place, so
/// the detection layer can hold a reference without locking.
/// </summary>
public sealed class ShortcutMap
{
    private readonly FrozenDictionary<ShortcutCommand, IReadOnlyList<ShortcutTrigger>> _byCommand;

    public ShortcutMap(IEnumerable<ShortcutBinding> bindings)
    {
        Bindings = bindings.ToArray();
        _byCommand = Bindings
            .GroupBy(b => b.Command)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<ShortcutTrigger>)g.Select(b => b.Trigger).ToArray());
    }

    public IReadOnlyList<ShortcutBinding> Bindings { get; }

    public IReadOnlyList<ShortcutTrigger> For(ShortcutCommand command) =>
        _byCommand.TryGetValue(command, out var triggers) ? triggers : [];

    /// <summary>
    /// Reproduces the §0.1 inventory exactly, with D2's side-agnostic modifiers, plus the three new
    /// bindings from §6.2. <see cref="ShortcutCommand.CommitSelection" /> has no default binding by
    /// design — it is derived (§5).
    /// </summary>
    public static ShortcutMap Default { get; } =
        new(
            [
                new ShortcutBinding(
                    ShortcutCommand.NextWindow,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Alt,
                        Key = new ShortcutKey(VirtualKeys.OemTilde),
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.PreviousWindow,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Alt | ShortcutModifiers.Shift,
                        Key = new ShortcutKey(VirtualKeys.OemTilde),
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.MediaWindow,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Alt | ShortcutModifiers.Ctrl,
                        Key = new ShortcutKey(0x47), // G
                    }
                ),
                // Win+Ctrl+Left must not reach the OS (it is a virtual-desktop shortcut), hence Suppress.
                new ShortcutBinding(
                    ShortcutCommand.DockWindow,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Win | ShortcutModifiers.Ctrl,
                        Key = new ShortcutKey(VirtualKeys.Left),
                        Suppress = true,
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.MinimizeWindow,
                    new ShortcutTrigger.KeyMouse
                    {
                        Modifiers = ShortcutModifiers.Ctrl | ShortcutModifiers.Alt,
                        Button = ShortcutMouseButton.Left,
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.MinimizeWindow,
                    new ShortcutTrigger.KeyMouse
                    {
                        Modifiers = ShortcutModifiers.Ctrl,
                        Button = ShortcutMouseButton.X2,
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.MaximizeWindow,
                    new ShortcutTrigger.KeyMouse
                    {
                        Modifiers = ShortcutModifiers.Ctrl | ShortcutModifiers.Alt,
                        Button = ShortcutMouseButton.Right,
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.MaximizeWindow,
                    new ShortcutTrigger.KeyMouse
                    {
                        Modifiers = ShortcutModifiers.Ctrl,
                        Button = ShortcutMouseButton.X1,
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.ThumbnailWindow,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Alt | ShortcutModifiers.Ctrl,
                        Key = new ShortcutKey(0x54), // T
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.SuspendedWindows,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Alt | ShortcutModifiers.Ctrl,
                        Key = new ShortcutKey(0x53), // S
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.SuspendWindow,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Alt | ShortcutModifiers.Ctrl | ShortcutModifiers.Shift,
                        Key = new ShortcutKey(0x53), // S
                    }
                ),
                new ShortcutBinding(
                    ShortcutCommand.ShowSettings,
                    new ShortcutTrigger.Keyboard
                    {
                        Modifiers = ShortcutModifiers.Alt | ShortcutModifiers.Ctrl,
                        Key = new ShortcutKey(VirtualKeys.OemComma),
                    }
                ),
            ]
        );

    /// <summary>
    /// Bindings from two or more <i>different</i> commands claiming the same physical input.
    /// <para>
    /// Grouped by <see cref="ShortcutTrigger.InputIdentity" /> rather than record equality, because
    /// generated record equality includes <c>Suppress</c> — <c>Alt+G {Suppress=true}</c> and
    /// <c>Alt+G {Suppress=false}</c> are unequal records that nonetheless fight over the same
    /// keystroke.
    /// </para>
    /// </summary>
    public IReadOnlyList<ShortcutConflict> FindConflicts()
    {
        return Bindings
            .GroupBy(b => b.Trigger.InputIdentity, StringComparer.Ordinal)
            .Select(g => new { Group = g, Commands = g.Select(b => b.Command).Distinct().ToArray() })
            .Where(x => x.Commands.Length > 1)
            .Select(x => new ShortcutConflict(x.Group.First().Trigger, x.Commands))
            .ToArray();
    }

    /// <summary>A copy with every binding for <paramref name="command" /> replaced.</summary>
    public ShortcutMap WithBindings(ShortcutCommand command, IEnumerable<ShortcutTrigger> triggers) =>
        new(
            Bindings.Where(b => b.Command != command).Concat(triggers.Select(t => new ShortcutBinding(command, t)))
        );
}
