using WinTabber.Events.Shortcuts;

namespace WinTabber.Events.Tests.Shortcuts;

/// <summary>§6.3: "Round-trip must be lossless; add a unit test asserting Parse(Format(k)) == k for
/// every VK in the table."</summary>
public class ShortcutKeyRoundTripTests
{
    [Test]
    public async Task Format_then_parse_round_trips_every_key_in_the_table()
    {
        foreach (var vk in ShortcutDisplayNames.KnownVirtualKeys)
        {
            var key = new ShortcutKey(vk);
            var formatted = ShortcutDisplayNames.Format(key);

            await Assert.That(ShortcutDisplayNames.TryParse(formatted, out var parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(key);
        }
    }

    [Test]
    public async Task Canonical_names_are_unique_across_the_table()
    {
        var names = ShortcutDisplayNames
            .KnownVirtualKeys.Select(vk => ShortcutDisplayNames.Format(new ShortcutKey(vk)))
            .ToList();

        await Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsEqualTo(names.Count);
    }

    [Test]
    public async Task Keys_outside_the_table_round_trip_through_the_hex_fallback()
    {
        // 0xFF (VK_OEM_CLEAR-adjacent / unassigned) is deliberately not in the canonical table.
        var key = new ShortcutKey(0xFF);
        var formatted = ShortcutDisplayNames.Format(key);

        await Assert.That(formatted).IsEqualTo("VK:0xFF");
        await Assert.That(ShortcutDisplayNames.TryParse(formatted, out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(key);
    }

    [Test]
    public async Task Parsing_junk_fails_without_throwing()
    {
        await Assert.That(ShortcutDisplayNames.TryParse("NotAKey", out _)).IsFalse();
        await Assert.That(ShortcutDisplayNames.TryParse("", out _)).IsFalse();
        await Assert.That(ShortcutDisplayNames.TryParse(null, out _)).IsFalse();
        await Assert.That(ShortcutDisplayNames.TryParse("VK:0xZZ", out _)).IsFalse();
    }

    [Test]
    public async Task The_plans_example_key_names_parse_to_the_expected_virtual_keys()
    {
        // These literal names appear in the §6.3 settings.json sample and must keep working.
        await Assert.That(Parse("OemTilde").VirtualKey).IsEqualTo((ushort)0xC0);
        await Assert.That(Parse("Left").VirtualKey).IsEqualTo((ushort)0x25);
        await Assert.That(Parse("G").VirtualKey).IsEqualTo((ushort)0x47);
        await Assert.That(Parse("T").VirtualKey).IsEqualTo((ushort)0x54);
        await Assert.That(Parse("S").VirtualKey).IsEqualTo((ushort)0x53);
        await Assert.That(Parse("OemComma").VirtualKey).IsEqualTo((ushort)0xBC);

        static ShortcutKey Parse(string name)
        {
            ShortcutDisplayNames.TryParse(name, out var key);
            return key;
        }
    }

    [Test]
    public async Task Modifier_keys_are_recognized_as_modifiers()
    {
        foreach (
            ushort vk in (ushort[])
                [
                    VirtualKeys.Control,
                    VirtualKeys.Menu,
                    VirtualKeys.Shift,
                    VirtualKeys.LControl,
                    VirtualKeys.RControl,
                    VirtualKeys.LMenu,
                    VirtualKeys.RMenu,
                    VirtualKeys.LShift,
                    VirtualKeys.RShift,
                    VirtualKeys.LWin,
                    VirtualKeys.RWin,
                ]
        )
        {
            await Assert.That(new ShortcutKey(vk).IsModifier).IsTrue();
        }

        await Assert.That(new ShortcutKey(VirtualKeys.OemTilde).IsModifier).IsFalse();
        await Assert.That(new ShortcutKey(VirtualKeys.Capital).IsModifier).IsFalse();
    }
}
