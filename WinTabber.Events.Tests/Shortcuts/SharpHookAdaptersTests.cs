using SharpHook.Data;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;

namespace WinTabber.Events.Tests.Shortcuts;

/// <summary>
/// Covers decision D2. The old <c>GetMods</c> read only <c>LeftCtrl/LeftAlt/LeftShift/LeftMeta</c>,
/// so right-hand modifiers matched nothing. These assertions pin the fix.
/// </summary>
public class SharpHookAdaptersTests
{
    [Test]
    public async Task Left_modifiers_map_to_the_side_agnostic_flags()
    {
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.LeftCtrl)).IsEqualTo(ShortcutModifiers.Ctrl);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.LeftAlt)).IsEqualTo(ShortcutModifiers.Alt);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.LeftShift)).IsEqualTo(ShortcutModifiers.Shift);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.LeftMeta)).IsEqualTo(ShortcutModifiers.Win);
    }

    [Test]
    public async Task Right_modifiers_map_identically_to_left_ones()
    {
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.RightCtrl)).IsEqualTo(ShortcutModifiers.Ctrl);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.RightAlt)).IsEqualTo(ShortcutModifiers.Alt);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.RightShift))
            .IsEqualTo(ShortcutModifiers.Shift);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.RightMeta)).IsEqualTo(ShortcutModifiers.Win);
    }

    [Test]
    public async Task A_single_modifier_side_is_enough()
    {
        // EventMask.Ctrl is defined as LeftCtrl|RightCtrl, so a HasFlag(EventMask.Ctrl) test would
        // wrongly demand that BOTH control keys are down. This is the regression guard for that.
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.Ctrl) & ShortcutModifiers.Ctrl)
            .IsEqualTo(ShortcutModifiers.Ctrl);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.LeftCtrl) & ShortcutModifiers.Ctrl)
            .IsEqualTo(ShortcutModifiers.Ctrl);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.RightCtrl) & ShortcutModifiers.Ctrl)
            .IsEqualTo(ShortcutModifiers.Ctrl);
    }

    [Test]
    public async Task Mixed_sides_combine()
    {
        var mask = EventMask.LeftCtrl | EventMask.RightAlt | EventMask.RightShift;

        await Assert.That(SharpHookAdapters.ToShortcutModifiers(mask))
            .IsEqualTo(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt | ShortcutModifiers.Shift);
    }

    [Test]
    public async Task Non_modifier_mask_bits_are_ignored()
    {
        var mask = EventMask.NumLock | EventMask.CapsLock | EventMask.ScrollLock | EventMask.Button1;

        await Assert.That(SharpHookAdapters.ToShortcutModifiers(mask)).IsEqualTo(ShortcutModifiers.None);
    }

    [Test]
    public async Task Simulated_and_suppress_bits_never_leak_into_the_modifier_set()
    {
        // SimulatedEvent and SuppressEvent live in the same Mask that carries the modifier bits.
        // Held-modifier tracking reads that mask on every event, so these must not register as
        // modifiers.
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.SimulatedEvent))
            .IsEqualTo(ShortcutModifiers.None);
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(EventMask.SuppressEvent))
            .IsEqualTo(ShortcutModifiers.None);

        // And they do not disturb genuine modifiers riding alongside them.
        var mask = EventMask.LeftAlt | EventMask.SimulatedEvent | EventMask.SuppressEvent;
        await Assert.That(SharpHookAdapters.ToShortcutModifiers(mask)).IsEqualTo(ShortcutModifiers.Alt);
    }

    [Test]
    public async Task Modifier_keys_report_their_own_bit_from_either_side()
    {
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcLeftControl)).IsEqualTo(ShortcutModifiers.Ctrl);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcRightControl)).IsEqualTo(ShortcutModifiers.Ctrl);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcLeftAlt)).IsEqualTo(ShortcutModifiers.Alt);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcRightAlt)).IsEqualTo(ShortcutModifiers.Alt);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcLeftShift)).IsEqualTo(ShortcutModifiers.Shift);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcRightShift)).IsEqualTo(ShortcutModifiers.Shift);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcLeftMeta)).IsEqualTo(ShortcutModifiers.Win);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcRightMeta)).IsEqualTo(ShortcutModifiers.Win);

        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcA)).IsEqualTo(ShortcutModifiers.None);
        await Assert.That(SharpHookAdapters.ToModifierBit(KeyCode.VcCapsLock)).IsEqualTo(ShortcutModifiers.None);
    }

    [Test]
    public async Task Mouse_buttons_map_to_the_documented_names()
    {
        await Assert.That(SharpHookAdapters.ToShortcutMouseButton(MouseButton.Button1))
            .IsEqualTo(ShortcutMouseButton.Left);
        await Assert.That(SharpHookAdapters.ToShortcutMouseButton(MouseButton.Button2))
            .IsEqualTo(ShortcutMouseButton.Right);
        await Assert.That(SharpHookAdapters.ToShortcutMouseButton(MouseButton.Button3))
            .IsEqualTo(ShortcutMouseButton.Middle);
        // Button4/Button5 are Mouse 4 / Mouse 5, matching the old XButton1/XButton2 wiring.
        await Assert.That(SharpHookAdapters.ToShortcutMouseButton(MouseButton.Button4))
            .IsEqualTo(ShortcutMouseButton.X1);
        await Assert.That(SharpHookAdapters.ToShortcutMouseButton(MouseButton.Button5))
            .IsEqualTo(ShortcutMouseButton.X2);
        await Assert.That(SharpHookAdapters.ToShortcutMouseButton(MouseButton.NoButton))
            .IsEqualTo(ShortcutMouseButton.None);
    }

    [Test]
    public async Task Global_hotkey_modifiers_never_include_NoRepeat()
    {
        // Auto-repeat is load-bearing: holding Alt+` must keep cycling through windows.
        var modifiers = SharpHookAdapters.ToGlobalModifiers(ShortcutModifiers.Alt | ShortcutModifiers.Shift);

        await Assert.That(modifiers.HasFlag(GlobalHotKeys.Native.Types.Modifiers.NoRepeat)).IsFalse();
        await Assert.That(modifiers.HasFlag(GlobalHotKeys.Native.Types.Modifiers.Alt)).IsTrue();
        await Assert.That(modifiers.HasFlag(GlobalHotKeys.Native.Types.Modifiers.Shift)).IsTrue();
        await Assert.That(modifiers.HasFlag(GlobalHotKeys.Native.Types.Modifiers.Control)).IsFalse();
    }

    [Test]
    public async Task Global_virtual_key_is_the_raw_vk_value()
    {
        await Assert.That((int)SharpHookAdapters.ToGlobalVirtualKey(new ShortcutKey(VirtualKeys.OemTilde)))
            .IsEqualTo(0xC0);
        await Assert.That((int)SharpHookAdapters.ToGlobalVirtualKey(new ShortcutKey(0x47))).IsEqualTo(0x47);
    }
}
