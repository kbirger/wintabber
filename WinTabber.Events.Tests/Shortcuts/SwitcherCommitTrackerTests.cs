using WinTabber.Events.Shortcuts;

namespace WinTabber.Events.Tests.Shortcuts;

/// <summary>Covers §5 — per-activation hold capture and commit derivation.</summary>
public class SwitcherCommitTrackerTests
{
    private static ShortcutActivation Activate(ShortcutCommand command, ShortcutModifiers mods, ushort vk) =>
        new(command, new ShortcutTrigger.Keyboard { Modifiers = mods, Key = new ShortcutKey(vk) });

    [Test]
    public async Task Releasing_the_activating_modifier_commits()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));

        await Assert.That(tracker.IsSwitcherActive).IsTrue();
        await Assert.That(tracker.ActiveHoldSet).IsEqualTo(ShortcutModifiers.Alt);

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsTrue();
    }

    [Test]
    public async Task Right_side_modifiers_commit_the_same_as_left_ones()
    {
        // D2: the model is side-agnostic, so the tracker never sees a "left Alt" vs "right Alt"
        // distinction. This is the deliberate fix for the existing right-modifier bug.
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsTrue();
    }

    [Test]
    public async Task Ctrl_tab_second_binding_does_not_wedge_the_switcher_open()
    {
        // §5's concrete failure mode for a map-wide hold set. NextWindow has a second binding of
        // Ctrl+Tab, so a union-derived hold set would be {Alt, Ctrl}. The user activates with
        // Alt+`, presses Ctrl mid-cycle, then releases Alt while keeping Ctrl held. With a
        // union set, {Ctrl} & {Alt,Ctrl} != 0 and the switcher would never commit.
        var map = ShortcutMap.Default.WithBindings(
            ShortcutCommand.NextWindow,
            [
                new ShortcutTrigger.Keyboard
                {
                    Modifiers = ShortcutModifiers.Alt,
                    Key = new ShortcutKey(VirtualKeys.OemTilde),
                },
                new ShortcutTrigger.Keyboard { Modifiers = ShortcutModifiers.Ctrl, Key = new ShortcutKey(VirtualKeys.Tab) },
            ]
        );
        await Assert.That(map.For(ShortcutCommand.NextWindow).Count).IsEqualTo(2);

        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));

        // Per-activation capture: only Alt, never {Alt, Ctrl}.
        await Assert.That(tracker.ActiveHoldSet).IsEqualTo(ShortcutModifiers.Alt);

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt | ShortcutModifiers.Ctrl)).IsFalse();

        // Alt released, Ctrl still down -> must still commit.
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Ctrl)).IsTrue();
    }

    [Test]
    public async Task Activating_with_the_second_binding_captures_that_bindings_modifiers()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Ctrl, VirtualKeys.Tab));

        await Assert.That(tracker.ActiveHoldSet).IsEqualTo(ShortcutModifiers.Ctrl);

        // Alt going up and down is irrelevant now.
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt)).IsTrue();
    }

    [Test]
    public async Task Cycling_while_open_does_not_recapture_the_hold_set()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));

        // Alt+Shift+` (PreviousWindow) pressed mid-cycle must not widen the hold set to {Alt,Shift},
        // or the user holding Shift after releasing Alt would wedge the switcher open.
        tracker.OnActivation(
            Activate(
                ShortcutCommand.PreviousWindow,
                ShortcutModifiers.Alt | ShortcutModifiers.Shift,
                VirtualKeys.OemTilde
            )
        );

        await Assert.That(tracker.ActiveHoldSet).IsEqualTo(ShortcutModifiers.Alt);
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Shift)).IsTrue();
    }

    [Test]
    public async Task Empty_hold_set_never_commits_and_requests_the_keyboard_fallback()
    {
        // §5 edge case: a modifier-less activating trigger (e.g. F13) has no release to commit on.
        // Without the Enter/Esc fallback the switcher would be unclosable.
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.None, 0x7C /* F13 */));

        await Assert.That(tracker.ActiveHoldSet).IsEqualTo(ShortcutModifiers.None);
        await Assert.That(tracker.RequiresKeyboardCommitFallback).IsTrue();

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Ctrl)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsFalse();
    }

    [Test]
    public async Task Fallback_is_not_requested_for_a_normal_activation()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));

        await Assert.That(tracker.RequiresKeyboardCommitFallback).IsFalse();
    }

    [Test]
    public async Task Non_switcher_commands_do_not_open_the_switcher()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.MediaWindow, ShortcutModifiers.Alt | ShortcutModifiers.Ctrl, 0x47));

        await Assert.That(tracker.IsSwitcherActive).IsFalse();
        await Assert.That(tracker.ActiveHoldSet).IsNull();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsFalse();
    }

    [Test]
    public async Task Modifier_release_while_the_switcher_is_closed_never_commits()
    {
        var tracker = new SwitcherCommitTracker();

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsFalse();
    }

    [Test]
    public async Task Closing_the_switcher_clears_the_hold_set()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));
        tracker.OnSwitcherClosed();

        await Assert.That(tracker.IsSwitcherActive).IsFalse();
        await Assert.That(tracker.ActiveHoldSet).IsNull();
        await Assert.That(tracker.RequiresKeyboardCommitFallback).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsFalse();
    }

    [Test]
    public async Task Commit_fires_only_once_per_activation()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsTrue();
        // Repeated zero-mask reports must not re-fire; only a fresh non-zero -> zero edge counts.
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsFalse();
    }

    [Test]
    public async Task Committing_closes_the_switcher()
    {
        // The tracker closes itself on commit so the caller does not have to route
        // CmdCommitSelection back through the merged command stream to notice — that would be a
        // cycle across an Rx RefCount boundary.
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsTrue();

        await Assert.That(tracker.IsSwitcherActive).IsFalse();
        await Assert.That(tracker.ActiveHoldSet).IsNull();

        // And a re-press genuinely reopens rather than being swallowed.
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));
        await Assert.That(tracker.IsSwitcherActive).IsTrue();
    }

    [Test]
    public async Task Multi_modifier_activation_commits_only_when_all_of_them_are_released()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(
            Activate(
                ShortcutCommand.PreviousWindow,
                ShortcutModifiers.Alt | ShortcutModifiers.Shift,
                VirtualKeys.OemTilde
            )
        );

        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt | ShortcutModifiers.Shift)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.Alt)).IsFalse();
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsTrue();
    }

    [Test]
    public async Task Reopening_after_a_close_captures_the_new_hold_set()
    {
        var tracker = new SwitcherCommitTracker();
        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Alt, VirtualKeys.OemTilde));
        tracker.OnSwitcherClosed();

        tracker.OnActivation(Activate(ShortcutCommand.NextWindow, ShortcutModifiers.Ctrl, VirtualKeys.Tab));

        await Assert.That(tracker.ActiveHoldSet).IsEqualTo(ShortcutModifiers.Ctrl);
        await Assert.That(tracker.OnHeldModifiersChanged(ShortcutModifiers.None)).IsTrue();
    }
}
