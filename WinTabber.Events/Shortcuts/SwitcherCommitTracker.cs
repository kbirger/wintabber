namespace WinTabber.Events.Shortcuts;

/// <summary>
/// Derives "release of the activating modifiers commits the switcher selection" (§5).
/// <para>
/// This is deliberately a pure state machine with no Rx, no SharpHook and no WPF: Phase 3 only has
/// to feed it three kinds of input. That also makes the tricky cases below unit-testable.
/// </para>
/// <para>
/// <b>The hold set is captured per activation and is never derived from the union or intersection
/// of the map's switcher bindings.</b> Any map-wide set mixes in modifiers from bindings that were
/// not used. Concrete failure: with a second <c>NextWindow</c> binding of <c>Ctrl+Tab</c>, a
/// map-wide set is <c>{Alt, Ctrl}</c>; the user activates with <c>Alt+`</c>, presses Ctrl
/// mid-cycle, releases Alt but keeps Ctrl held — <c>{Ctrl} &amp; {Alt,Ctrl} != 0</c>, so the
/// switcher never commits and is stuck open. Per-activation capture is also what Alt-Tab itself
/// does, and it means nothing needs recomputing when the <see cref="ShortcutMap" /> changes.
/// </para>
/// </summary>
public sealed class SwitcherCommitTracker
{
    private ShortcutModifiers _held;
    private ShortcutModifiers _lastMaskedHold;

    /// <summary>Whether the switcher is currently open.</summary>
    public bool IsSwitcherActive { get; private set; }

    /// <summary>
    /// The modifier set of the trigger that opened the switcher, or null while it is closed.
    /// </summary>
    public ShortcutModifiers? ActiveHoldSet { get; private set; }

    public ShortcutModifiers HeldModifiers => _held;

    /// <summary>
    /// True when the switcher is open but the activating trigger carried no modifiers (e.g. a bare
    /// <c>F13</c> binding), so there is no release to commit on.
    /// <para>
    /// The switcher window must then handle <c>Enter</c> (commit) and <c>Esc</c> (cancel) itself.
    /// Without that fallback the switcher would be unclosable — do not skip it.
    /// </para>
    /// </summary>
    public bool RequiresKeyboardCommitFallback => IsSwitcherActive && ActiveHoldSet == ShortcutModifiers.None;

    /// <summary>
    /// Feed every activation here. Only the first switcher-opening activation captures the hold
    /// set; subsequent Next/Previous presses while the switcher is already open cycle the selection
    /// without disturbing it.
    /// </summary>
    public void OnActivation(ShortcutActivation activation)
    {
        if (!activation.Command.OpensSwitcher())
        {
            return;
        }

        if (IsSwitcherActive)
        {
            return;
        }

        IsSwitcherActive = true;
        ActiveHoldSet = activation.Trigger.Modifiers;

        // Seed from the trigger's own modifiers rather than the last observed held mask: the
        // activation is proof they were down, and the hook may not have reported them yet.
        _lastMaskedHold = activation.Trigger.Modifiers;
    }

    /// <summary>
    /// Feed the live held-modifier set here.
    /// <para>
    /// On commit the tracker closes itself. That is deliberate: the caller emits
    /// <see cref="EventType.CmdCommitSelection" /> onto the same merged command stream it would
    /// otherwise have to subscribe to in order to notice the close, and routing the close back
    /// through that stream would create a cycle across an Rx <c>RefCount</c> boundary. Callers only
    /// need to report the *other* ways a switcher closes (window selected, app hidden).
    /// </para>
    /// </summary>
    /// <returns>
    /// True exactly when <c>held &amp; ActiveHoldSet</c> transitions from non-zero to zero while the
    /// switcher is active — i.e. the caller should emit <see cref="EventType.CmdCommitSelection" />.
    /// </returns>
    public bool OnHeldModifiersChanged(ShortcutModifiers held)
    {
        _held = held;

        if (!IsSwitcherActive || ActiveHoldSet is not { } holdSet || holdSet == ShortcutModifiers.None)
        {
            return false;
        }

        var masked = held & holdSet;
        var wasHeld = _lastMaskedHold != ShortcutModifiers.None;
        _lastMaskedHold = masked;

        if (!wasHeld || masked != ShortcutModifiers.None)
        {
            return false;
        }

        OnSwitcherClosed();
        return true;
    }

    /// <summary>Clear the hold set when the switcher closes, for any reason.</summary>
    public void OnSwitcherClosed()
    {
        IsSwitcherActive = false;
        ActiveHoldSet = null;
        _lastMaskedHold = ShortcutModifiers.None;
    }
}
