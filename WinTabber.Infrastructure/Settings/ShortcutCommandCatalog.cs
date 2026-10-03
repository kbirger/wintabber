using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinTabber.Events.Shortcuts;

namespace WinTabber.Infrastructure.Settings;

/// <summary>
/// The name, description, group and icon that <see cref="ShortcutCommand" /> shows in the settings
/// UI. Loaded once from the embedded <c>Resources/ShortcutCommands.json</c> file so a new command
/// only needs a JSON entry, not a new C# switch arm.
/// </summary>
public sealed class ShortcutCommandCatalogEntry
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("group")]
    public string Group { get; init; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "";
}

public static class ShortcutCommandCatalog
{
    private const string ResourceName = "WinTabber.Infrastructure.ShortcutCommands.json";

    private static readonly Dictionary<string, ShortcutCommandCatalogEntry> Entries = Load();

    private static Dictionary<string, ShortcutCommandCatalogEntry> Load()
    {
        using var stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{ResourceName}' was not found.");

        var entries = JsonSerializer.Deserialize<Dictionary<string, ShortcutCommandCatalogEntry>>(stream);
        return entries ?? throw new InvalidOperationException($"'{ResourceName}' deserialized to null.");
    }

    private static ShortcutCommandCatalogEntry For(ShortcutCommand command)
    {
        var id = command.ToPersistedId();
        if (!Entries.TryGetValue(id, out var entry))
        {
            throw new KeyNotFoundException($"No ShortcutCommands.json entry for '{id}'.");
        }

        return entry;
    }

    public static string GetDisplayName(this ShortcutCommand command) => For(command).DisplayName;

    public static string GetDescription(this ShortcutCommand command) => For(command).Description;

    public static string GetGroupName(this ShortcutCommand command) => For(command).Group;

    public static IconKey GetIcon(this ShortcutCommand command)
    {
        var entry = For(command);
        if (!Enum.TryParse<IconKey>(entry.Icon, out var icon))
        {
            throw new InvalidOperationException($"IconKey has no member named '{entry.Icon}'.");
        }

        return icon;
    }
}
