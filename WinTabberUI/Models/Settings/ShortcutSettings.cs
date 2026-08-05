using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinTabber.Events.Shortcuts;

namespace WinTabberUI.Models.Settings;

/// <summary>
/// The <c>Shortcuts</c> block of settings.json (§6.3).
/// <para>
/// Deliberately tolerant: unknown command keys are ignored rather than fatal (forward compat), and
/// a binding that fails to parse falls back to that command's default rather than throwing. A
/// hand-editable block of key-name strings makes malformed input a realistic case, and one typo
/// must not take down all settings loading.
/// </para>
/// </summary>
public class ShortcutSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Command persistence id (<see cref="ShortcutCommandExtensions.ToPersistedId" />) to its list
    /// of triggers. A missing command entry falls back to that command's default.
    /// </summary>
    public Dictionary<string, List<ShortcutTriggerDto>> Bindings { get; set; } = new();

    public static ShortcutSettings FromMap(ShortcutMap map)
    {
        var settings = new ShortcutSettings { Version = CurrentVersion };

        foreach (var command in Enum.GetValues<ShortcutCommand>())
        {
            var triggers = map.For(command);
            if (triggers.Count == 0 && ShortcutMap.Default.For(command).Count == 0)
            {
                // Never bound by default and still unbound — nothing worth writing.
                continue;
            }

            settings.Bindings[command.ToPersistedId()] = triggers.Select(ShortcutTriggerDto.FromTrigger).ToList();
        }

        return settings;
    }

    /// <summary>
    /// Materializes a <see cref="ShortcutMap" />. Never throws: every failure path degrades to the
    /// default binding for the affected command.
    /// </summary>
    public ShortcutMap ToMap()
    {
        var bindings = new List<ShortcutBinding>();

        foreach (var command in Enum.GetValues<ShortcutCommand>())
        {
            var id = command.ToPersistedId();

            if (!Bindings.TryGetValue(id, out var dtos) || dtos is null)
            {
                // Missing command entry -> that command's default (§6.3).
                bindings.AddRange(ShortcutMap.Default.For(command).Select(t => new ShortcutBinding(command, t)));
                continue;
            }

            var triggers = new List<ShortcutTrigger>();
            var failed = false;

            foreach (var dto in dtos)
            {
                if (dto is not null && dto.TryToTrigger(out var trigger))
                {
                    triggers.Add(trigger);
                }
                else
                {
                    failed = true;
                    Debug.WriteLine($"Shortcut settings: unparseable binding for '{id}'; falling back to default.");
                }
            }

            if (failed)
            {
                // Per-binding parse failure falls back to this command's default, not to a partial
                // list — a half-applied keymap is more confusing than a known-good one.
                bindings.AddRange(ShortcutMap.Default.For(command).Select(t => new ShortcutBinding(command, t)));
            }
            else
            {
                bindings.AddRange(triggers.Select(t => new ShortcutBinding(command, t)));
            }
        }

        // Unknown keys in Bindings are simply never read — forward compat, not an error.
        return new ShortcutMap(bindings);
    }
}

/// <summary>
/// Wire shape for a <see cref="ShortcutTrigger" />. A hand-written DTO rather than a polymorphic
/// <c>JsonConverter</c> over the record hierarchy: the discriminator handling stays in one obvious
/// place, and unknown/absent fields degrade instead of throwing.
/// <para>
/// Only <c>"Keyboard"</c> and <c>"KeyMouse"</c> exist. There is no <c>"ModifierRelease"</c> —
/// commit-on-release is derived (§5), never bound, so no such trigger can ever be constructed.
/// </para>
/// </summary>
public class ShortcutTriggerDto
{
    public const string KeyboardType = "Keyboard";
    public const string KeyMouseType = "KeyMouse";

    public string Type { get; set; } = KeyboardType;

    /// <summary>The flags enum as a comma-joined string — human-editable and ordinal-stable.</summary>
    public string Modifiers { get; set; } = nameof(ShortcutModifiers.None);

    /// <summary>Canonical key name (e.g. <c>OemTilde</c>), not a number. Keyboard triggers only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Button { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Edge { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Suppress { get; set; }

    public static ShortcutTriggerDto FromTrigger(ShortcutTrigger trigger) =>
        trigger switch
        {
            ShortcutTrigger.Keyboard keyboard => new ShortcutTriggerDto
            {
                Type = KeyboardType,
                Modifiers = FormatModifiers(keyboard.Modifiers),
                Key = ShortcutDisplayNames.Format(keyboard.Key),
                Edge = keyboard.Edge == TriggerEdge.Release ? nameof(TriggerEdge.Release) : null,
                Suppress = keyboard.Suppress,
            },
            ShortcutTrigger.KeyMouse mouse => new ShortcutTriggerDto
            {
                Type = KeyMouseType,
                Modifiers = FormatModifiers(mouse.Modifiers),
                Button = mouse.Button.ToString(),
                Suppress = mouse.Suppress,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown trigger shape."),
        };

    public bool TryToTrigger(out ShortcutTrigger trigger)
    {
        trigger = null!;

        if (!Enum.TryParse<ShortcutModifiers>(Modifiers, ignoreCase: true, out var modifiers))
        {
            return false;
        }

        if (string.Equals(Type, KeyboardType, StringComparison.OrdinalIgnoreCase))
        {
            if (!ShortcutDisplayNames.TryParse(Key, out var key) || key.IsNone || key.IsModifier)
            {
                return false;
            }

            var edge = TriggerEdge.Press;
            if (!string.IsNullOrWhiteSpace(Edge) && !Enum.TryParse(Edge, ignoreCase: true, out edge))
            {
                return false;
            }

            trigger = new ShortcutTrigger.Keyboard
            {
                Modifiers = modifiers,
                Key = key,
                Edge = edge,
                // Risk mitigation (§8): never suppress a bare key, or a bad binding can trap the user.
                Suppress = Suppress && modifiers != ShortcutModifiers.None,
            };
            return true;
        }

        if (string.Equals(Type, KeyMouseType, StringComparison.OrdinalIgnoreCase))
        {
            if (
                !Enum.TryParse<ShortcutMouseButton>(Button, ignoreCase: true, out var button)
                || button == ShortcutMouseButton.None
            )
            {
                return false;
            }

            trigger = new ShortcutTrigger.KeyMouse
            {
                Modifiers = modifiers,
                Button = button,
                Suppress = Suppress && modifiers != ShortcutModifiers.None,
            };
            return true;
        }

        // Unknown discriminator (including a stale "ModifierRelease" from a hand-edited file).
        return false;
    }

    private static string FormatModifiers(ShortcutModifiers modifiers) =>
        modifiers == ShortcutModifiers.None
            ? nameof(ShortcutModifiers.None)
            : string.Join(", ", ShortcutDisplayNames.Split(modifiers));
}
