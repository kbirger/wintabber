using System.Collections.Frozen;

namespace WinTabber.Events.Shortcuts;

/// <summary>
/// Canonical name &lt;-&gt; virtual-key table plus human-facing display glyphs.
/// <para>
/// Two distinct concepts live here on purpose:
/// <list type="bullet">
///   <item><b>Canonical name</b> — the stable, ASCII, persistence-safe identifier written to
///   settings.json (e.g. <c>OemTilde</c>). Round-trips losslessly.</item>
///   <item><b>Display name</b> — what the user sees on a chip (e.g. <c>`</c>). Not parseable, not
///   persisted, and may change freely.</item>
/// </list>
/// </para>
/// <para>
/// Canonical names deliberately match the WPF <c>System.Windows.Input.Key</c> member names so the
/// WPF-side fallback (<c>KeyInterop.KeyFromVirtualKey</c>) agrees with this table. This file itself
/// stays WPF-free so the model can be referenced from non-WPF assemblies.
/// </para>
/// </summary>
public static class ShortcutDisplayNames
{
    private static readonly FrozenDictionary<ushort, string> _canonicalByVk;
    private static readonly FrozenDictionary<string, ushort> _vkByCanonical;
    private static readonly FrozenDictionary<ushort, string> _glyphByVk;

    static ShortcutDisplayNames()
    {
        var canonical = new Dictionary<ushort, string>();

        void Add(ushort vk, string name) => canonical[vk] = name;

        // Letters A-Z (VK 0x41-0x5A) — VK code equals the ASCII uppercase letter.
        for (ushort vk = 0x41; vk <= 0x5A; vk++)
        {
            Add(vk, ((char)vk).ToString());
        }

        // Digits 0-9 (VK 0x30-0x39). Named D0..D9 to match the WPF Key enum.
        for (ushort vk = 0x30; vk <= 0x39; vk++)
        {
            Add(vk, "D" + (char)vk);
        }

        // F1-F24 (VK 0x70-0x87).
        for (ushort vk = 0x70; vk <= 0x87; vk++)
        {
            Add(vk, "F" + (vk - 0x6F));
        }

        // NumPad0-9 (VK 0x60-0x69).
        for (ushort vk = 0x60; vk <= 0x69; vk++)
        {
            Add(vk, "NumPad" + (vk - 0x60));
        }

        Add(VirtualKeys.Back, "Back");
        Add(VirtualKeys.Tab, "Tab");
        Add(VirtualKeys.Return, "Return");
        Add(VirtualKeys.Pause, "Pause");
        Add(VirtualKeys.Capital, "Capital");
        Add(VirtualKeys.Escape, "Escape");
        Add(VirtualKeys.Space, "Space");
        Add(VirtualKeys.PageUp, "PageUp");
        Add(VirtualKeys.PageDown, "PageDown");
        Add(VirtualKeys.End, "End");
        Add(VirtualKeys.Home, "Home");
        Add(VirtualKeys.Left, "Left");
        Add(VirtualKeys.Up, "Up");
        Add(VirtualKeys.Right, "Right");
        Add(VirtualKeys.Down, "Down");
        Add(VirtualKeys.PrintScreen, "PrintScreen");
        Add(VirtualKeys.Insert, "Insert");
        Add(VirtualKeys.Delete, "Delete");
        Add(VirtualKeys.Apps, "Apps");
        Add(VirtualKeys.Multiply, "Multiply");
        Add(VirtualKeys.Add, "Add");
        Add(VirtualKeys.Subtract, "Subtract");
        Add(VirtualKeys.Decimal, "Decimal");
        Add(VirtualKeys.Divide, "Divide");
        Add(VirtualKeys.NumLock, "NumLock");
        Add(VirtualKeys.Scroll, "Scroll");
        Add(VirtualKeys.OemSemicolon, "OemSemicolon");
        Add(VirtualKeys.OemPlus, "OemPlus");
        Add(VirtualKeys.OemComma, "OemComma");
        Add(VirtualKeys.OemMinus, "OemMinus");
        Add(VirtualKeys.OemPeriod, "OemPeriod");
        Add(VirtualKeys.OemQuestion, "OemQuestion");
        Add(VirtualKeys.OemTilde, "OemTilde");
        Add(VirtualKeys.OemOpenBrackets, "OemOpenBrackets");
        Add(VirtualKeys.OemPipe, "OemPipe");
        Add(VirtualKeys.OemCloseBrackets, "OemCloseBrackets");
        Add(VirtualKeys.OemQuotes, "OemQuotes");

        _canonicalByVk = canonical.ToFrozenDictionary();
        _vkByCanonical = canonical.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        _glyphByVk = new Dictionary<ushort, string>
        {
            [VirtualKeys.OemTilde] = "`",
            [VirtualKeys.OemMinus] = "-",
            [VirtualKeys.OemPlus] = "=",
            [VirtualKeys.OemComma] = ",",
            [VirtualKeys.OemPeriod] = ".",
            [VirtualKeys.OemSemicolon] = ";",
            [VirtualKeys.OemQuestion] = "/",
            [VirtualKeys.OemOpenBrackets] = "[",
            [VirtualKeys.OemCloseBrackets] = "]",
            [VirtualKeys.OemPipe] = "\\",
            [VirtualKeys.OemQuotes] = "'",
            [VirtualKeys.Left] = "←",
            [VirtualKeys.Up] = "↑",
            [VirtualKeys.Right] = "→",
            [VirtualKeys.Down] = "↓",
            [VirtualKeys.Return] = "Enter",
            [VirtualKeys.Back] = "Backspace",
            [VirtualKeys.Capital] = "Caps Lock",
            [VirtualKeys.PageUp] = "Page Up",
            [VirtualKeys.PageDown] = "Page Down",
            [VirtualKeys.PrintScreen] = "Print Screen",
            [VirtualKeys.NumLock] = "Num Lock",
            [VirtualKeys.Scroll] = "Scroll Lock",
            [VirtualKeys.Multiply] = "NumPad *",
            [VirtualKeys.Add] = "NumPad +",
            [VirtualKeys.Subtract] = "NumPad -",
            [VirtualKeys.Decimal] = "NumPad .",
            [VirtualKeys.Divide] = "NumPad /",
        }.ToFrozenDictionary();
    }

    /// <summary>All virtual keys in the canonical table. Used by the round-trip test.</summary>
    public static IReadOnlyCollection<ushort> KnownVirtualKeys => _canonicalByVk.Keys;

    /// <summary>Canonical, persistence-safe name for a key, or null if the key is not in the table.</summary>
    public static string? GetCanonicalName(ShortcutKey key) =>
        _canonicalByVk.TryGetValue(key.VirtualKey, out var name) ? name : null;

    /// <summary>
    /// Serialize a key to its canonical name. Keys outside the table fall back to a
    /// <c>VK:0x00</c> form so persistence stays lossless even for exotic hardware keys.
    /// </summary>
    public static string Format(ShortcutKey key) => GetCanonicalName(key) ?? $"VK:0x{key.VirtualKey:X2}";

    /// <summary>Inverse of <see cref="Format" />. Returns false for unrecognized input.</summary>
    public static bool TryParse(string? name, out ShortcutKey key)
    {
        key = ShortcutKey.None;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        name = name.Trim();
        if (_vkByCanonical.TryGetValue(name, out var vk))
        {
            key = new ShortcutKey(vk);
            return true;
        }

        if (name.StartsWith("VK:0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = name.AsSpan(5);
            if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var raw))
            {
                key = new ShortcutKey(raw);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Human-facing label for a key. Falls back to the canonical name, which is why the WPF-side
    /// <c>KeyInterop</c> fallback only has to handle keys missing from this table entirely.
    /// </summary>
    public static string GetDisplayName(ShortcutKey key)
    {
        if (_glyphByVk.TryGetValue(key.VirtualKey, out var glyph))
        {
            return glyph;
        }

        if (_canonicalByVk.TryGetValue(key.VirtualKey, out var canonical))
        {
            // "D0".."D9" display as the bare digit.
            if (canonical.Length == 2 && canonical[0] == 'D' && char.IsAsciiDigit(canonical[1]))
            {
                return canonical[1].ToString();
            }

            return canonical;
        }

        return $"0x{key.VirtualKey:X2}";
    }

    /// <summary>Canonical modifier render order (§4): Ctrl, Alt, Shift, Win — always.</summary>
    public static IReadOnlyList<ShortcutModifiers> OrderedModifiers { get; } =
        [ShortcutModifiers.Ctrl, ShortcutModifiers.Alt, ShortcutModifiers.Shift, ShortcutModifiers.Win];

    public static string GetDisplayName(ShortcutModifiers modifier) =>
        modifier switch
        {
            ShortcutModifiers.Ctrl => "Ctrl",
            ShortcutModifiers.Alt => "Alt",
            ShortcutModifiers.Shift => "Shift",
            ShortcutModifiers.Win => "Win",
            _ => modifier.ToString(),
        };

    public static string GetDisplayName(ShortcutMouseButton button) =>
        button switch
        {
            ShortcutMouseButton.Left => "Left Click",
            ShortcutMouseButton.Right => "Right Click",
            ShortcutMouseButton.Middle => "Middle Click",
            ShortcutMouseButton.X1 => "Mouse 4",
            ShortcutMouseButton.X2 => "Mouse 5",
            _ => "None",
        };

    /// <summary>Enumerates the set bits of <paramref name="modifiers" /> in canonical order.</summary>
    public static IEnumerable<ShortcutModifiers> Split(ShortcutModifiers modifiers) =>
        OrderedModifiers.Where(m => modifiers.HasFlag(m));
}
