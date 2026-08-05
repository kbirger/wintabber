namespace WinTabber.Events.Shortcuts;

/// <summary>
/// A single trigger. Exactly two shapes exist: <see cref="Keyboard" /> (modifiers + one
/// non-modifier key) and <see cref="KeyMouse" /> (modifiers + one mouse button).
/// <para>
/// There is deliberately <b>no ModifierRelease shape</b>. Commit-on-modifier-release is derived
/// per-activation (see <see cref="SwitcherCommitTracker" />), never user-bound, so nothing would
/// ever construct one; and <c>Keyboard { Edge = Release }</c> already covers "fire when this
/// specific key is released". Do not add one, and do not add a <c>"Type": "ModifierRelease"</c>
/// branch to the JSON converter.
/// </para>
/// <para>
/// Value records, so equality is free — the settings UI needs it for conflict detection and the
/// matcher needs it for dictionary lookup. Note that generated equality includes
/// <see cref="Keyboard.Suppress" />; conflict detection must use <see cref="InputIdentity" />
/// instead, which excludes it.
/// </para>
/// </summary>
public abstract record ShortcutTrigger
{
    public required ShortcutModifiers Modifiers { get; init; }

    /// <summary>
    /// RegisterHotKey-eligible iff the shape is <see cref="Keyboard" />, the edge is
    /// <see cref="TriggerEdge.Press" />, suppression is off, and the key is a real non-modifier key
    /// (§2.3). Everything else routes through the SharpHook hook.
    /// </summary>
    public abstract bool IsHotKeyEligible { get; }

    /// <summary>
    /// Whether the trigger swallows the input so downstream applications never see it.
    /// </summary>
    public abstract bool SuppressInput { get; }

    /// <summary>
    /// Identity of the *physical input* this trigger claims, excluding <c>Suppress</c>.
    /// <para>
    /// Two bindings conflict when their <see cref="InputIdentity" /> matches — record equality is
    /// the wrong test, because <c>Alt+G {Suppress=true}</c> and <c>Alt+G {Suppress=false}</c> are
    /// unequal records yet claim the same keystroke. <see cref="TriggerEdge" /> stays part of the
    /// identity: press and release of the same key are genuinely different inputs.
    /// </para>
    /// </summary>
    public abstract string InputIdentity { get; }

    /// <summary>Keyboard-only: modifiers + one non-modifier key.</summary>
    public sealed record Keyboard : ShortcutTrigger
    {
        public required ShortcutKey Key { get; init; }
        public TriggerEdge Edge { get; init; } = TriggerEdge.Press;

        /// <summary>Swallow the input from downstream apps. Forces hook routing.</summary>
        public bool Suppress { get; init; }

        public override bool IsHotKeyEligible =>
            Edge == TriggerEdge.Press && !Suppress && !Key.IsNone && !Key.IsModifier;

        public override bool SuppressInput => Suppress;

        public override string InputIdentity => $"K|{(int)Modifiers}|{Key.VirtualKey}|{(int)Edge}";

        public override string ToString() => ShortcutFormatting.Describe(this);
    }

    /// <summary>Modifiers + exactly one mouse button. Always hook-based.</summary>
    public sealed record KeyMouse : ShortcutTrigger
    {
        public required ShortcutMouseButton Button { get; init; }
        public bool Suppress { get; init; }

        public override bool IsHotKeyEligible => false;

        public override bool SuppressInput => Suppress;

        public override string InputIdentity => $"M|{(int)Modifiers}|{(int)Button}";

        public override string ToString() => ShortcutFormatting.Describe(this);
    }
}

/// <summary>Plain-text rendering of a trigger, for logging and <c>ToString</c>.</summary>
public static class ShortcutFormatting
{
    public static string Describe(ShortcutTrigger? trigger)
    {
        if (trigger is null)
        {
            return "Not set";
        }

        var parts = ShortcutDisplayNames.Split(trigger.Modifiers).Select(ShortcutDisplayNames.GetDisplayName).ToList();

        switch (trigger)
        {
            case ShortcutTrigger.Keyboard keyboard:
                if (!keyboard.Key.IsNone)
                {
                    parts.Add(ShortcutDisplayNames.GetDisplayName(keyboard.Key));
                }
                if (keyboard.Edge == TriggerEdge.Release)
                {
                    parts.Add("(release)");
                }
                break;
            case ShortcutTrigger.KeyMouse mouse:
                parts.Add(ShortcutDisplayNames.GetDisplayName(mouse.Button));
                break;
        }

        return parts.Count == 0 ? "Not set" : string.Join(" + ", parts);
    }
}
