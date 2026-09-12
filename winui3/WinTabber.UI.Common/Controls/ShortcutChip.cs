using WinTabber.Events.Shortcuts;

namespace WinTabber.UI.Common.Controls;

public enum ChipKind
{
    Modifier,
    Key,
    Mouse,
    Hint,
}

public sealed record ShortcutChip(string Text, ChipKind Kind);

/// <summary>
/// The WinUI3 half of the display-name story. <see cref="ShortcutDisplayNames" /> covers every key
/// the app can bind and stays UI-framework-free so the model is referenceable from non-UI
/// assemblies; this adds a WinRT <c>VirtualKey</c> fallback for exotic keys outside that table
/// (media, volume, browser keys), and turns a trigger into the chip list the presenter renders.
/// </summary>
public static class ShortcutChips
{
    public static string GetDisplayName(ShortcutKey key)
    {
        if (ShortcutDisplayNames.GetCanonicalName(key) is not null)
        {
            return ShortcutDisplayNames.GetDisplayName(key);
        }

        // Outside the canonical table — ask WinRT what it thinks this virtual key is.
        var vk = (Windows.System.VirtualKey)key.VirtualKey;
        if (Enum.IsDefined(vk) && vk != Windows.System.VirtualKey.None)
        {
            return vk.ToString();
        }

        return ShortcutDisplayNames.GetDisplayName(key);
    }

    /// <summary>
    /// Chips for a trigger, with modifiers always in the canonical Ctrl, Alt, Shift, Win order
    /// regardless of the order the user pressed them.
    /// </summary>
    public static IReadOnlyList<ShortcutChip> Build(ShortcutTrigger? trigger, bool showEdgeHint)
    {
        if (trigger is null)
        {
            return [];
        }

        var chips = ShortcutDisplayNames
            .Split(trigger.Modifiers)
            .Select(m => new ShortcutChip(ShortcutDisplayNames.GetDisplayName(m), ChipKind.Modifier))
            .ToList();

        switch (trigger)
        {
            case ShortcutTrigger.Keyboard keyboard:
                if (!keyboard.Key.IsNone)
                {
                    chips.Add(new ShortcutChip(GetDisplayName(keyboard.Key), ChipKind.Key));
                }

                if (showEdgeHint && keyboard.Edge == TriggerEdge.Release)
                {
                    chips.Add(new ShortcutChip("release", ChipKind.Hint));
                }
                break;

            case ShortcutTrigger.KeyMouse mouse:
                chips.Add(new ShortcutChip(ShortcutDisplayNames.GetDisplayName(mouse.Button), ChipKind.Mouse));
                break;
        }

        return chips;
    }

    /// <summary>Chips for an in-progress capture: modifiers only, nothing committed yet.</summary>
    public static IReadOnlyList<ShortcutChip> BuildInProgress(ShortcutModifiers modifiers) =>
        ShortcutDisplayNames
            .Split(modifiers)
            .Select(m => new ShortcutChip(ShortcutDisplayNames.GetDisplayName(m), ChipKind.Modifier))
            .ToList();
}
