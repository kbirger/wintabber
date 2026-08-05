using WinTabber.Events.Shortcuts;

namespace WinTabber.Events.Tests.Shortcuts;

public class ShortcutMapTests
{
    private static ShortcutTrigger.Keyboard Key(ShortcutModifiers mods, ushort vk, bool suppress = false) =>
        new()
        {
            Modifiers = mods,
            Key = new ShortcutKey(vk),
            Suppress = suppress,
        };

    [Test]
    public async Task Default_map_has_no_conflicts()
    {
        // §6.2 asks to verify Alt+Ctrl+T / Alt+Ctrl+S / Alt+Ctrl+, do not collide with the
        // pre-existing §0.1 bindings before finalizing them. This is that verification.
        var conflicts = ShortcutMap.Default.FindConflicts();

        await Assert
            .That(conflicts)
            .IsEmpty()
            .Because(string.Join("; ", conflicts.Select(c => $"{c.Trigger} <- {string.Join(",", c.Commands)}")));
    }

    [Test]
    public async Task Default_map_reproduces_the_existing_inventory()
    {
        var map = ShortcutMap.Default;

        // Commands 5 and 6 each have *two* triggers today; a one-binding-per-command schema would
        // have silently dropped half the existing behavior.
        await Assert.That(map.For(ShortcutCommand.MinimizeWindow).Count).IsEqualTo(2);
        await Assert.That(map.For(ShortcutCommand.MaximizeWindow).Count).IsEqualTo(2);

        await Assert.That(map.For(ShortcutCommand.NextWindow).Count).IsEqualTo(1);
        await Assert.That(map.For(ShortcutCommand.NextWindow)[0]).IsEqualTo(Key(ShortcutModifiers.Alt, 0xC0));

        // CommitSelection is derived (§5), never bound.
        await Assert.That(map.For(ShortcutCommand.CommitSelection)).IsEmpty();
    }

    [Test]
    public async Task Default_dock_binding_suppresses_and_is_therefore_hook_routed()
    {
        var dock = ShortcutMap.Default.For(ShortcutCommand.DockWindow).Single();

        await Assert.That(dock.SuppressInput).IsTrue();
        await Assert.That(dock.IsHotKeyEligible).IsFalse();
    }

    [Test]
    public async Task Conflicts_are_found_when_two_commands_claim_the_same_input()
    {
        var map = new ShortcutMap(
            [
                new ShortcutBinding(ShortcutCommand.NextWindow, Key(ShortcutModifiers.Alt, 0x47)),
                new ShortcutBinding(ShortcutCommand.MediaWindow, Key(ShortcutModifiers.Alt, 0x47)),
            ]
        );

        var conflicts = map.FindConflicts();

        await Assert.That(conflicts.Count).IsEqualTo(1);
        await Assert.That(conflicts[0].Commands).Contains(ShortcutCommand.NextWindow);
        await Assert.That(conflicts[0].Commands).Contains(ShortcutCommand.MediaWindow);
    }

    [Test]
    public async Task Conflict_detection_ignores_Suppress()
    {
        // Record equality includes Suppress, so these two are *unequal records* — yet they fight
        // over the same physical keystroke. Grouping by record equality would let this slip through.
        var suppressing = Key(ShortcutModifiers.Alt, 0x47, suppress: true);
        var plain = Key(ShortcutModifiers.Alt, 0x47);

        await Assert.That(suppressing).IsNotEqualTo(plain);

        var map = new ShortcutMap(
            [
                new ShortcutBinding(ShortcutCommand.NextWindow, suppressing),
                new ShortcutBinding(ShortcutCommand.MediaWindow, plain),
            ]
        );

        await Assert.That(map.FindConflicts().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Press_and_release_of_the_same_key_are_not_a_conflict()
    {
        var press = new ShortcutTrigger.Keyboard
        {
            Modifiers = ShortcutModifiers.Alt,
            Key = new ShortcutKey(0x47),
            Edge = TriggerEdge.Press,
        };
        var release = press with { Edge = TriggerEdge.Release };

        var map = new ShortcutMap(
            [
                new ShortcutBinding(ShortcutCommand.NextWindow, press),
                new ShortcutBinding(ShortcutCommand.MediaWindow, release),
            ]
        );

        await Assert.That(map.FindConflicts()).IsEmpty();
    }

    [Test]
    public async Task A_command_bound_twice_to_the_same_input_is_not_reported_as_a_conflict()
    {
        var map = new ShortcutMap(
            [
                new ShortcutBinding(ShortcutCommand.NextWindow, Key(ShortcutModifiers.Alt, 0x47)),
                new ShortcutBinding(ShortcutCommand.NextWindow, Key(ShortcutModifiers.Alt, 0x47)),
            ]
        );

        await Assert.That(map.FindConflicts()).IsEmpty();
    }

    [Test]
    public async Task Mouse_and_keyboard_triggers_never_collide_with_each_other()
    {
        var map = new ShortcutMap(
            [
                new ShortcutBinding(ShortcutCommand.NextWindow, Key(ShortcutModifiers.Ctrl, 0x01)),
                new ShortcutBinding(
                    ShortcutCommand.MinimizeWindow,
                    new ShortcutTrigger.KeyMouse
                    {
                        Modifiers = ShortcutModifiers.Ctrl,
                        Button = ShortcutMouseButton.Left,
                    }
                ),
            ]
        );

        await Assert.That(map.FindConflicts()).IsEmpty();
    }

    [Test]
    public async Task HotKey_eligibility_follows_the_four_rules()
    {
        // 1. shape is Keyboard, 2. Edge == Press, 3. !Suppress, 4. Key != None and !Key.IsModifier
        await Assert.That(Key(ShortcutModifiers.Alt, 0x47).IsHotKeyEligible).IsTrue();
        await Assert.That(Key(ShortcutModifiers.Alt, 0x47, suppress: true).IsHotKeyEligible).IsFalse();
        await Assert
            .That((Key(ShortcutModifiers.Alt, 0x47) with { Edge = TriggerEdge.Release }).IsHotKeyEligible)
            .IsFalse();
        await Assert.That(Key(ShortcutModifiers.Alt, 0).IsHotKeyEligible).IsFalse();
        await Assert.That(Key(ShortcutModifiers.None, VirtualKeys.LControl).IsHotKeyEligible).IsFalse();
        await Assert
            .That(
                new ShortcutTrigger.KeyMouse
                {
                    Modifiers = ShortcutModifiers.Ctrl,
                    Button = ShortcutMouseButton.X1,
                }.IsHotKeyEligible
            )
            .IsFalse();
    }

    [Test]
    public async Task WithBindings_replaces_every_binding_for_one_command_only()
    {
        var map = ShortcutMap.Default.WithBindings(
            ShortcutCommand.MinimizeWindow,
            [Key(ShortcutModifiers.Ctrl | ShortcutModifiers.Shift, 0x4D)]
        );

        await Assert.That(map.For(ShortcutCommand.MinimizeWindow).Count).IsEqualTo(1);
        await Assert.That(map.For(ShortcutCommand.MaximizeWindow).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Every_shortcut_command_maps_to_a_distinct_event_type()
    {
        var eventTypes = Enum.GetValues<ShortcutCommand>().Select(c => c.ToEventType()).ToList();

        await Assert.That(eventTypes.Distinct().Count()).IsEqualTo(eventTypes.Count);
    }

    [Test]
    public async Task Persisted_command_ids_round_trip()
    {
        foreach (var command in Enum.GetValues<ShortcutCommand>())
        {
            await Assert.That(ShortcutCommandExtensions.TryParsePersistedId(command.ToPersistedId(), out var parsed))
                .IsTrue();
            await Assert.That(parsed).IsEqualTo(command);
        }

        // Unknown ids are ignored, not fatal (§6.3 forward compat).
        await Assert.That(ShortcutCommandExtensions.TryParsePersistedId("SomeFutureCommand", out _)).IsFalse();
    }

    [Test]
    public async Task CommitSelection_is_not_offered_as_a_bindable_command()
    {
        await Assert.That(ShortcutCommandExtensions.Bindable).DoesNotContain(ShortcutCommand.CommitSelection);
        await Assert.That(ShortcutCommandExtensions.Bindable.Count).IsEqualTo(Enum.GetValues<ShortcutCommand>().Length - 1);
    }
}
