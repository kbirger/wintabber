# Hint-overlay system: WinUI 3 port design

Date: 2026-09-17
Status: proposed

## Purpose

The WPF app has a Vimium-style keyboard hint overlay (`HintBehavior`). Press
Alt, badges appear over interactive controls, type the badge's text to
activate that control. This document designs its WinUI 3 replacement.

## Real-world scope

Hints exist in exactly one place today: `MediaControlsWindow` and its
`VolumeControls` user control, in `WinTabber.UI.Media`. No other WPF window
uses hints. The winui3 port targets the same scope: the media controls
window's buttons, the play/pause and mute toggles, the volume sliders, and
the three `ComboBox`es (session, playback device, recording device),
including their per-item hints.

## Why this is not a mechanical port

The WPF implementation depends on three things WinUI 3 does not have in the
same form:

- `AdornerLayer`/`Adorner` for the overlay surface.
- `PreviewKeyDown`, a tunneling key event, for Alt interception.
- WPF UI Automation peer interfaces (`IInvokeProvider`, `IExpandCollapseProvider`,
  `IToggleProvider`) reached through `FrameworkElementAutomationPeer`.

## Key finding: WinUI 3 has a native equivalent

WinUI 3 ships `Microsoft.UI.Xaml.Input.AccessKeyManager`, the same feature as
`HintBehavior`: press Alt, the framework shows key-tip badges over elements
with an `AccessKey` set, filters them as the user types, and raises
`AccessKeyInvoked` on a match. It also has the same automation peer and
provider types WPF uses (`FrameworkElementAutomationPeer`,
`ButtonAutomationPeer`, `ComboBoxAutomationPeer`, `IInvokeProvider`,
`IExpandCollapseProvider`, `IToggleProvider`), confirmed directly against the
Windows App SDK's `Microsoft.UI.Xaml.winmd` metadata.

This changes the port from "build a custom overlay subsystem" to "wire up a
framework feature and add a small amount of glue for the dynamic case."

## Architecture

### Fixed controls: no code

Every static hint (`Button`, `ToggleButton`, `Slider`'s associated toggle)
gets `AccessKey="X"` set directly in XAML, using the same key strings the
WPF app already assigns:

| Control | WPF hint | WinUI 3 |
|---|---|---|
| Prev button | `"B"` | `AccessKey="B"` |
| Play/pause toggle | `"P"` | `AccessKey="P"` |
| Next button | `"F"` | `AccessKey="F"` |
| Session `ComboBox` | `"A"` | `AccessKey="A"` |
| Playback device `ComboBox` | `"S"` | `AccessKey="S"` |
| Recording device `ComboBox` | `"R"` | `AccessKey="R"` |
| Volume slider / mute toggle | `"PM"`/`"DV"`/`"DM"` (chords) | `AccessKey` on the toggle + `AccessKeyScopeOwner` on the parent, matching the chord |

Verified live: pressing Alt shows the badge, and for `Button`/`ToggleButton`,
pressing the key performs the control's default action (`Click`/toggle) with
no handler code at all. This is the bulk of the feature and costs no new
code.

### Activation for controls needing custom dispatch

Where the default `AccessKeyInvoked` action is not what is needed (see
below), a handler sets `args.Handled = true` and drives the control through
its automation peer, the same dispatch table `HintBehavior.ActivateElement`
used, ported to `Microsoft.UI.Xaml.Automation.*`:

- `Button` → default action (`Click`) is correct as-is; no handler needed.
- `ToggleButton` → default action (toggles `IsChecked`, which the app's
  `Command` binding already reacts to) is correct as-is; no handler needed.
- `ComboBox` (opening the drop-down) → default action is correct; see below
  for why its own hint mode still needs a small assist.
- `ComboBoxItem` (selecting a session/device from an open drop-down) →
  **no useful default** (pressing its key only highlights it, confirmed by
  a live spike). Needs an explicit handler.

### Dynamic per-item hints: `DynamicAccessKeyScope`

`ComboBox` has no `ContainerContentChanging` (that event exists only on
`ItemsControl` subclasses further down the hierarchy than `ComboBox`
inherits from usefully here — confirmed by a build error during the spike,
not assumed). Item containers only exist once the drop-down opens. A small,
reusable static helper replaces WPF's `IHintBehaviorKernel` /
`DefaultHintBehaviorKernel` / `ItemsControlHintBehaviorKernel` strategy
pattern entirely:

```csharp
namespace WinTabber.UI.Common.AccessKeys;

public static class DynamicAccessKeyScope
{
    public static void AttachSequentialKeys(
        ComboBox owner,
        Action<ComboBoxItem, int> onActivated)
    {
        owner.IsAccessKeyScope = true;
        owner.ExitDisplayModeOnAccessKeyInvoked = false;

        var wired = new HashSet<ComboBoxItem>();
        owner.DropDownOpened += (_, _) => owner.DispatcherQueue.TryEnqueue(() =>
        {
            for (int i = 0; i < owner.Items.Count; i++)
            {
                if (owner.ContainerFromIndex(i) is ComboBoxItem container)
                {
                    container.AccessKey = (i + 1).ToString();
                    if (wired.Add(container))
                    {
                        var index = i;
                        container.AccessKeyInvoked += (_, args) =>
                        {
                            onActivated(container, index);
                            args.Handled = true;
                        };
                    }
                }
            }
            AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
        });
    }
}
```

Each of the three `ComboBox`es wires up with one call in its window's
constructor, e.g.:

```csharp
DynamicAccessKeyScope.AttachSequentialKeys(SessionSelector, (container, index) =>
{
    SessionSelector.SelectedIndex = index;
    SessionSelector.IsDropDownOpen = false;
});
```

Design notes, each backed by a live spike against the running app, not
assumption:

- `ExitDisplayModeOnAccessKeyInvoked = false` plus the explicit
  `AccessKeyManager.EnterDisplayMode` call after assigning item keys is
  required to keep the badge/chord session continuous across the transition
  from the `ComboBox`'s own key into its items' keys. Without it, the user
  must press Alt a second time after opening the drop-down.
- The `HashSet<ComboBoxItem>` dedup is required because `DropDownOpened`
  fires on every open and containers can be reused; without it, handlers
  stack across repeated opens.
- `onActivated` is a plain delegate, not an interface: each `ComboBox`'s
  "what does selecting an item mean" is one or two lines, with nothing
  shared to abstract yet.
- `AttachSequentialKeys` is typed to `ComboBox` specifically, not a generic
  `ItemsControl` helper, because `ComboBox` is the only real consumer today.
  A future window-selector case most likely uses a `ListView`/
  `ItemsRepeater`, which does have `ContainerContentChanging` and so needs
  its own, differently-shaped helper — not a forced abstraction over both
  before a second consumer exists.

### Known accepted gap: default badge redraw

A live spike (instrumented with `AccessKeyManager.IsDisplayModeEnabled` /
`IsDisplayModeEnabledChanged` logging, read directly from Visual Studio's
Debug Output via the `vs-debug` tools rather than manual reproduction) found
that default key-tip *badges* stop visually redrawing for a `ComboBox`'s
items after its first open in a session, even though the underlying
`AccessKeyManager` state machine stays healthy (`IsDisplayModeEnabled`
remains `True`, chord matching keeps working) and **activation keeps working
every time** — confirmed by pressing the digit key with no visible badge and
having it still select the item. This is a rendering-only gap in the
framework's default badge, not a functional one, and is superseded once the
follow-up custom-rendering task (below) replaces default badges outright.

## Deferred to a follow-up task

Per explicit instruction, this design covers functionality only. A follow-up
task will add custom badge visuals via `AccessKeyDisplayRequested`/
`AccessKeyDisplayDismissed`, which:

- Replace the default key-tip badge with a `HintAdorner`-styled visual
  (rounded rect, accent-color fill, positioned via `TransformToVisual`).
- Use `AccessKeyDisplayRequestedEventArgs.PressedKeys` (confirmed present in
  the SDK metadata) to reproduce the WPF original's live prefix-highlight
  behavior as the user types a chord.
- Incidentally resolve the default-badge-redraw gap above, since custom
  rendering replaces the default badge entirely.

## Explicit exclusions

Not ported, and not planned for a follow-up:

- `GeneratedHintsProvider` — zero call sites in the WPF app; its
  two-character overflow path is broken.
- `HintAdorner`'s commented-out substring-highlight draw call — dead code in
  the original.
- `HintPosition`, `HintChordState`, `HintActivationScope`, `HintAdorner`,
  `IHintBehaviorKernel` and its two implementations — fully replaced by
  `AccessKeyManager`; no WinUI 3 equivalent is needed.

## Testing

The WPF split (`HintBehaviorTests` headless vs.
`HintBehaviorDesktopTests` `[Category("RequiresDesktop")]`) tested a state
machine that no longer exists on our side. The only real logic left to test
is `DynamicAccessKeyScope.AttachSequentialKeys`: a desktop-requiring test
with a real `ComboBox` in a real `Window`, verifying keys are assigned on
`DropDownOpened` and `onActivated` fires with the correct index. Chord
matching, scope ownership, and display-mode transitions are framework
behavior and are not re-tested.
