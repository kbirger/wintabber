using System.Text.Json;
using WinTabber.Events.Shortcuts;
using WinTabberUI.Models.Settings;

namespace WinTabber.Infrastructure.Tests.Settings;

/// <summary>Covers §6.3 — the settings.json <c>Shortcuts</c> block.</summary>
public class ShortcutSettingsTests
{
    private static ShortcutSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<ShortcutSettings>(json)!;

    [Test]
    public async Task Default_map_survives_a_full_json_round_trip()
    {
        var json = JsonSerializer.Serialize(ShortcutSettings.FromMap(ShortcutMap.Default));
        var map = Deserialize(json).ToMap();

        foreach (var command in Enum.GetValues<ShortcutCommand>())
        {
            await Assert.That(map.For(command)).IsEquivalentTo(ShortcutMap.Default.For(command));
        }
    }

    [Test]
    public async Task Serialized_form_matches_the_documented_shape()
    {
        var json = JsonSerializer.Serialize(ShortcutSettings.FromMap(ShortcutMap.Default));

        await Assert.That(json).Contains("\"NextWindow\"");
        await Assert.That(json).Contains("\"Type\":\"Keyboard\"");
        await Assert.That(json).Contains("\"Key\":\"OemTilde\"");
        await Assert.That(json).Contains("\"Modifiers\":\"Alt, Shift\"");
        await Assert.That(json).Contains("\"Type\":\"KeyMouse\"");
        await Assert.That(json).Contains("\"Button\":\"X2\"");

        // There is no ModifierRelease shape and nothing may ever emit one.
        await Assert.That(json).DoesNotContain("ModifierRelease");
    }

    [Test]
    public async Task The_plans_example_block_parses()
    {
        const string json = """
            {
              "Version": 1,
              "Bindings": {
                "NextWindow":      [ { "Type": "Keyboard", "Modifiers": "Alt",        "Key": "OemTilde" } ],
                "PreviousWindow":  [ { "Type": "Keyboard", "Modifiers": "Alt, Shift", "Key": "OemTilde" } ],
                "DockWindow":      [ { "Type": "Keyboard", "Modifiers": "Win, Ctrl",  "Key": "Left", "Suppress": true } ],
                "MinimizeWindow":  [ { "Type": "KeyMouse", "Modifiers": "Ctrl, Alt",  "Button": "Left" },
                                     { "Type": "KeyMouse", "Modifiers": "Ctrl",       "Button": "X2"   } ],
                "MaximizeWindow":  [ { "Type": "KeyMouse", "Modifiers": "Ctrl, Alt",  "Button": "Right" },
                                     { "Type": "KeyMouse", "Modifiers": "Ctrl",       "Button": "X1"   } ],
                "MediaWindow":     [ { "Type": "Keyboard", "Modifiers": "Alt, Ctrl",  "Key": "G" } ],
                "ThumbnailWindow": [ { "Type": "Keyboard", "Modifiers": "Alt, Ctrl",  "Key": "T" } ],
                "SuspendedWindows":[ { "Type": "Keyboard", "Modifiers": "Alt, Ctrl",  "Key": "S" } ]
              }
            }
            """;

        var map = Deserialize(json).ToMap();

        await Assert.That(map.For(ShortcutCommand.MinimizeWindow).Count).IsEqualTo(2);
        await Assert.That(map.For(ShortcutCommand.DockWindow).Single().SuppressInput).IsTrue();
        await Assert.That(map.FindConflicts()).IsEmpty();
    }

    [Test]
    public async Task Unknown_command_keys_are_ignored_not_fatal()
    {
        const string json = """
            { "Version": 1, "Bindings": {
                "SomeFutureCommand": [ { "Type": "Keyboard", "Modifiers": "Alt", "Key": "Q" } ]
            } }
            """;

        var map = Deserialize(json).ToMap();

        // Everything falls back to defaults; the unknown key contributes nothing.
        await Assert.That(map.For(ShortcutCommand.NextWindow)).IsEquivalentTo(
            ShortcutMap.Default.For(ShortcutCommand.NextWindow)
        );
    }

    [Test]
    public async Task A_missing_command_entry_falls_back_to_that_commands_default()
    {
        const string json = """
            { "Version": 1, "Bindings": {
                "NextWindow": [ { "Type": "Keyboard", "Modifiers": "Ctrl", "Key": "Tab" } ]
            } }
            """;

        var map = Deserialize(json).ToMap();

        await Assert.That(map.For(ShortcutCommand.NextWindow).Count).IsEqualTo(1);
        await Assert.That(map.For(ShortcutCommand.MediaWindow)).IsEquivalentTo(
            ShortcutMap.Default.For(ShortcutCommand.MediaWindow)
        );
    }

    [Test]
    public async Task An_explicitly_empty_list_unbinds_the_command()
    {
        const string json = """{ "Version": 1, "Bindings": { "NextWindow": [] } }""";

        await Assert.That(Deserialize(json).ToMap().For(ShortcutCommand.NextWindow)).IsEmpty();
    }

    [Test]
    public async Task A_bad_key_name_falls_back_to_that_commands_default_instead_of_throwing()
    {
        const string json = """
            { "Version": 1, "Bindings": {
                "NextWindow": [ { "Type": "Keyboard", "Modifiers": "Alt", "Key": "NotARealKey" } ]
            } }
            """;

        await Assert.That(Deserialize(json).ToMap().For(ShortcutCommand.NextWindow)).IsEquivalentTo(
            ShortcutMap.Default.For(ShortcutCommand.NextWindow)
        );
    }

    [Test]
    public async Task A_bad_modifier_name_falls_back_to_that_commands_default()
    {
        const string json = """
            { "Version": 1, "Bindings": {
                "MediaWindow": [ { "Type": "Keyboard", "Modifiers": "Hyper", "Key": "G" } ]
            } }
            """;

        await Assert.That(Deserialize(json).ToMap().For(ShortcutCommand.MediaWindow)).IsEquivalentTo(
            ShortcutMap.Default.For(ShortcutCommand.MediaWindow)
        );
    }

    [Test]
    public async Task A_stale_ModifierRelease_discriminator_is_rejected_and_falls_back()
    {
        const string json = """
            { "Version": 1, "Bindings": {
                "NextWindow": [ { "Type": "ModifierRelease", "Modifiers": "Alt" } ]
            } }
            """;

        await Assert.That(Deserialize(json).ToMap().For(ShortcutCommand.NextWindow)).IsEquivalentTo(
            ShortcutMap.Default.For(ShortcutCommand.NextWindow)
        );
    }

    [Test]
    public async Task A_modifier_key_cannot_be_bound_as_the_key_part()
    {
        // "LeftCtrl" is not a canonical name at all; "Capital" is, and is not a modifier, so the
        // rejection here has to come from the canonical-name lookup failing.
        const string json = """
            { "Version": 1, "Bindings": {
                "NextWindow": [ { "Type": "Keyboard", "Modifiers": "Alt", "Key": "LeftCtrl" } ]
            } }
            """;

        await Assert.That(Deserialize(json).ToMap().For(ShortcutCommand.NextWindow)).IsEquivalentTo(
            ShortcutMap.Default.For(ShortcutCommand.NextWindow)
        );
    }

    [Test]
    public async Task Suppress_is_never_honored_for_a_bare_key()
    {
        // Risk table (§8): "Only honor Suppress for triggers with >=1 modifier; never suppress bare keys."
        const string json = """
            { "Version": 1, "Bindings": {
                "NextWindow": [ { "Type": "Keyboard", "Modifiers": "None", "Key": "F13", "Suppress": true } ]
            } }
            """;

        await Assert.That(Deserialize(json).ToMap().For(ShortcutCommand.NextWindow).Single().SuppressInput).IsFalse();
    }

    [Test]
    public async Task Release_edge_round_trips()
    {
        var map = ShortcutMap.Default.WithBindings(
            ShortcutCommand.ShowSettings,
            [
                new ShortcutTrigger.Keyboard
                {
                    Modifiers = ShortcutModifiers.Alt,
                    Key = new ShortcutKey(VirtualKeys.OemComma),
                    Edge = TriggerEdge.Release,
                },
            ]
        );

        var json = JsonSerializer.Serialize(ShortcutSettings.FromMap(map));
        var restored = (ShortcutTrigger.Keyboard)Deserialize(json).ToMap().For(ShortcutCommand.ShowSettings).Single();

        await Assert.That(restored.Edge).IsEqualTo(TriggerEdge.Release);
    }

    [Test]
    public async Task An_entirely_absent_Shortcuts_block_yields_the_default_map()
    {
        var settings = JsonSerializer.Deserialize<ApplicationSettings>("""{ "General": {} }""")!;

        await Assert.That(settings.Shortcuts.ToMap().Bindings.Count).IsEqualTo(ShortcutMap.Default.Bindings.Count);
    }
}
