using WinTabber.Events.Shortcuts;
using WinTabber.UI.Common.Controls;

namespace WinTabber.UI.Common.Tests.Controls;

public class ShortcutChipsTests
{
    [Test]
    public async Task Build_NullTrigger_ReturnsEmpty()
    {
        var chips = ShortcutChips.Build(null, showEdgeHint: true);

        await Assert.That(chips).IsEmpty();
    }

    [Test]
    public async Task Build_KeyboardTrigger_ProducesModifierAndKeyChips()
    {
        var trigger = new ShortcutTrigger.Keyboard
        {
            Modifiers = ShortcutModifiers.Ctrl | ShortcutModifiers.Alt,
            Key = new ShortcutKey(VirtualKeys.Delete),
        };

        var chips = ShortcutChips.Build(trigger, showEdgeHint: true);

        await Assert.That(chips.Select(c => c.Text)).IsEquivalentTo(["Ctrl", "Alt", "Delete"]);
        await Assert.That(chips[^1].Kind).IsEqualTo(ChipKind.Key);
    }

    [Test]
    public async Task Build_ReleaseEdgeWithHint_AppendsHintChip()
    {
        var trigger = new ShortcutTrigger.Keyboard
        {
            Modifiers = ShortcutModifiers.None,
            Key = new ShortcutKey(VirtualKeys.Delete),
            Edge = TriggerEdge.Release,
        };

        var chips = ShortcutChips.Build(trigger, showEdgeHint: true);

        await Assert.That(chips[^1]).IsEqualTo(new ShortcutChip("release", ChipKind.Hint));
    }

    [Test]
    public async Task BuildInProgress_ReturnsOnlyModifierChipsInCanonicalOrder()
    {
        var chips = ShortcutChips.BuildInProgress(ShortcutModifiers.Win | ShortcutModifiers.Ctrl);

        await Assert.That(chips.Select(c => c.Text)).IsEquivalentTo(["Ctrl", "Win"]);
    }

    [Test]
    public async Task GetDisplayName_KeyInCanonicalTable_UsesCanonicalDisplayName()
    {
        // VirtualKeys.Delete is in ShortcutDisplayNames' canonical table, so this must never reach
        // the VirtualKey fallback — pins the "canonical table wins" branch independent of whatever
        // Task 2b.1 Step 3 resolves for the fallback branch.
        var name = ShortcutChips.GetDisplayName(new ShortcutKey(VirtualKeys.Delete));

        await Assert.That(name).IsEqualTo(ShortcutDisplayNames.GetDisplayName(new ShortcutKey(VirtualKeys.Delete)));
    }
}
