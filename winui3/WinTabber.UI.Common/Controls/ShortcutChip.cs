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
/// assemblies; this adds friendly names for volume/media keys (which are NOT defined members of
/// <c>Windows.System.VirtualKey</c> — that enum stops at <c>GoHome = 0xAC</c>) plus a WinRT
/// <c>VirtualKey</c> fallback for other exotic keys outside the canonical table that DO have
/// defined members there (e.g. browser navigation keys, under different names than WPF's
/// <c>KeyInterop</c> gave them), and turns a trigger into the chip list the presenter renders.
/// </summary>
public static class ShortcutChips
{
    /// <summary>
    /// Volume and media keys (VK 0xAD-0xB3) are not defined members of
    /// <c>Windows.System.VirtualKey</c> (it stops at <c>GoHome = 0xAC</c>), so
    /// <see cref="Enum.IsDefined{TEnum}(TEnum)" /> returns false for them and the code would
    /// otherwise fall through all the way to <see cref="ShortcutDisplayNames.GetDisplayName" />'s
    /// raw hex fallback (e.g. "0xB3" for Play/Pause). Checked before the VirtualKey fallback.
    /// </summary>
    private static readonly Dictionary<ushort, string> VolumeAndMediaKeyNames = new()
    {
        [0xAD] = "Volume Mute",
        [0xAE] = "Volume Down",
        [0xAF] = "Volume Up",
        [0xB0] = "Next Track",
        [0xB1] = "Previous Track",
        [0xB2] = "Media Stop",
        [0xB3] = "Play/Pause",
    };

    public static string GetDisplayName(ShortcutKey key)
    {
        if (ShortcutDisplayNames.GetCanonicalName(key) is not null)
        {
            return ShortcutDisplayNames.GetDisplayName(key);
        }

        if (VolumeAndMediaKeyNames.TryGetValue(key.VirtualKey, out var mediaName))
        {
            return mediaName;
        }

        // Outside the canonical table and the volume/media table — ask WinRT what it thinks this
        // virtual key is.
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
