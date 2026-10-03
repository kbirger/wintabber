# Access-key custom badge rendering: WinUI 3 design

Date: 2026-09-23
Status: proposed

## Purpose

`docs/superpowers/specs/2026-09-17-hint-overlay-winui3-design.md` ported the WPF app's
Vimium-style keyboard hint overlay (`HintBehavior`/`HintAdorner`) to WinUI 3's native
`AccessKeyManager`, but deliberately deferred one piece: the default framework key-tip
badge does not look like `HintAdorner`'s badge, and does not reproduce its live
typed-prefix underline. This document designs that follow-up, and covers exactly the
three items that spec's "Deferred to a follow-up task" section named:

1. Replace the default key-tip badge with a `HintAdorner`-styled visual (rounded rect,
   accent-color fill, positioned via `TransformToVisual`).
2. Use `AccessKeyDisplayRequestedEventArgs.PressedKeys` to reproduce the WPF original's
   live prefix-underline behavior as the user types a chord.
3. Incidentally resolve the "Known accepted gap" from the parent spec — default key-tip
   badges stop visually redrawing for a `ComboBox`'s items after its first open in a
   session — since custom rendering replaces the default badge outright.

Nothing else is in scope. Real-world scope is unchanged from the parent spec: hints exist
in exactly one place, `MediaControlsWindow` and its `VolumeControls` user control.

## API surface, verified against the actual installed package

Verified against `Microsoft.UI.Xaml.xml`/`Microsoft.WinUI.dll` in the pinned
`Microsoft.WindowsAppSDK` version (`1.8.251106002`, per `Directory.Packages.props`) rather
than assumed, matching this migration's established practice of confirming WinUI 3 API
shape directly instead of guessing from WPF familiarity:

- **`Microsoft.UI.Xaml.Input.AccessKeyManager.AreKeyTipsEnabled`** — a static property.
  "Gets or sets a value that specifies whether KeyTips are shown for access keys." This is
  the mechanism that lets custom rendering *replace* the default badge rather than merely
  draw on top of it: setting it `false` suppresses every default key-tip badge app-wide,
  while `UIElement.AccessKeyDisplayRequested`/`AccessKeyDisplayDismissed` continue to fire
  normally per element, since the docs do not tie those events to this switch.
- **No exposed style or template exists for the default key-tip badge.** The only
  customization points on `UIElement`/`Control` are placement/offset properties
  (`KeyTipPlacementMode`, `KeyTipHorizontalOffset`, `KeyTipVerticalOffset`,
  `Control.IsTemplateKeyTipTarget`) — nothing that reaches the badge's visual template.
  This rules out "restyle the default badge in place" as an approach; genuinely custom
  drawing is the only way to change its look.
- **`UIElement.AccessKeyDisplayRequested`/`AccessKeyDisplayDismissed` do NOT bubble.**
  Corrected after review: an earlier draft of this design assumed they did, "consistent
  with the rest of WinUI 3's routed-event model." That was wrong, caught before
  implementation rather than discovered live. The actual evidence: `AccessKeyInvokedEventArgs`
  (a sibling event already in use, in `DynamicAccessKeyScope.cs`) has a `Handled` property —
  the standard signal for a routed event a handler can intercept mid-route. Neither
  `AccessKeyDisplayRequestedEventArgs` nor `AccessKeyDisplayDismissedEventArgs` has one —
  `AccessKeyDisplayRequestedEventArgs` has only `PressedKeys`, and
  `AccessKeyDisplayDismissedEventArgs` has no members at all beyond a constructor. This
  matches a worked example's own wiring pattern, which attaches both handlers directly on
  the specific element (`<Button AccessKeyDisplayRequested="..." .../>`), not at a
  container. **Every element that should get a custom badge must be individually
  registered** — there is no single root-level subscription that reaches every hinted
  descendant the way `HintBehavior`'s one attachment point did in WPF.
- **`AccessKeyDisplayRequestedEventArgs.PressedKeys`** — "Gets the keys that were pressed
  to start the access key sequence." Confirmed (via a worked example the user supplied,
  consistent with the documented member) to refire per keystroke as a chord is typed: on
  Alt press it is empty; after the user presses the first character of a multi-character
  access key, the event fires again for every element whose key starts with that
  character, with `PressedKeys` now containing what was typed so far.
- **`AccessKeyDisplayDismissedEventArgs`** carries no members beyond a constructor — it is
  a pure signal, fired on sequence completion, on Escape, or whenever display mode
  otherwise ends.

## Architecture

### `AreKeyTipsEnabled = false` at startup

One line, added wherever `winui3/WinTabberUI`'s app bootstrap already runs (`App.xaml.cs`
or `Bootstrapper.cs`):

```csharp
Microsoft.UI.Xaml.Input.AccessKeyManager.AreKeyTipsEnabled = false;
```

This is global and app-wide. Since hints exist in exactly one window today, nothing else
is affected. Leave a one-line comment at the call site explaining why this must stay
`false` — a future "cleanup" that removes it would silently bring back the default badge
alongside the custom one, both drawn at once.

### `AccessKeyBadgeLayer`

New class, `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`, next to the
existing `DynamicAccessKeyScope`. Wraps one `Canvas` that a window places, topmost, in its
own XAML — the drawing surface, playing the same role `AdornerLayer` played for
`HintAdorner` in WPF.

```csharp
namespace WinTabber.UI.Common.AccessKeys;

public sealed class AccessKeyBadgeLayer
{
    public AccessKeyBadgeLayer(Canvas overlay);

    /// <summary>Registers one element to get a custom badge. Since AccessKeyDisplayRequested/
    /// Dismissed do not bubble, every hinted element in a window needs its own call.</summary>
    public void Watch(UIElement element);
}
```

- `overlay` is the `Canvas` badges are added to and positioned within, via
  `Canvas.SetLeft`/`Canvas.SetTop`.
- `Watch` is called once per hinted element — every `Button`/`ToggleButton`/`Slider`/
  `ComboBox` with a static `AccessKey` in a window's XAML, plus, for the dynamic case,
  once per `ComboBoxItem` at the point `DynamicAccessKeyScope` assigns it a key (see
  below). There is no "attach at the root and let it bubble" shortcut.

Internal state: a `Dictionary<UIElement, AccessKeyBadge>` mapping a watched element to its
currently-visible badge, so `AccessKeyDisplayRequested` can find-or-create and
`AccessKeyDisplayDismissed` can find-and-remove.

### `DynamicAccessKeyScope` gains one new parameter

Since `ComboBoxItem`s are only reachable once a drop-down opens, and each one needs its own
`Watch` call the same as any static element, `AttachSequentialKeys` takes the badge layer
and calls `Watch` on each newly-realized container, guarded by the same `wired` `HashSet`
dedup that already protects `AccessKeyInvoked` from stacking across repeated opens:

```csharp
public static void AttachSequentialKeys(
    ComboBox owner,
    AccessKeyBadgeLayer badgeLayer,
    Action<ComboBoxItem, int> onActivated)
{
    // ... unchanged up to the wired.Add(container) check ...
    if (wired.Add(container))
    {
        badgeLayer.Watch(container);
        container.AccessKeyInvoked += (_, args) => { /* unchanged */ };
    }
    // ... unchanged ...
}
```

### Every window's hinted elements need a name to register

`MediaControlsWindow.xaml` today has two `Button`s (prev/next) and two `VolumeControls`
instances with no `x:Name` — nothing to call `Watch` on from code-behind without one.
`VolumeControls.xaml`'s `Slider`/`ToggleButton` are in the same position. All four gain an
`x:Name`, and `VolumeControls` gains a small method so its window doesn't need to reach
into its internals:

```csharp
// VolumeControls.xaml.cs
public void RegisterAccessKeyBadges(AccessKeyBadgeLayer layer)
{
    layer.Watch(VolumeSlider);
    layer.Watch(MuteToggleButton);
}
```

### `AccessKeyBadge`

New class, same folder, the visual itself — built in code, not XAML, since instances are
created and destroyed dynamically per active hint:

```csharp
namespace WinTabber.UI.Common.AccessKeys;

internal sealed class AccessKeyBadge
{
    public AccessKeyBadge(string text);
    public UIElement Visual { get; }
    public void UpdatePrefix(int underlineLength); // -1 hides the badge entirely

    /// <summary>Pure logic, unit-tested independent of any visual: how much of <paramref name="accessKey"/>
    /// to underline given what has been typed so far, or -1 if <paramref name="pressedKeys"/> is not
    /// a prefix of it.</summary>
    internal static int ComputeUnderlineLength(string accessKey, string pressedKeys);
}
```

A `Border` (4px corner radius, ~5px/4px padding, accent-color fill, accent-dark-1px
border) containing a `TextBlock` (bold, ~10px), ported directly from `HintAdorner`'s
constants and `OnRender` layout math (border sized to fit the widest glyph, text centered
within it). `UpdatePrefix` sets the `TextBlock`'s `TextDecorations` to underline the first
`underlineLength` characters, or sets `Visual.Visibility = Collapsed` when
`underlineLength == -1` (the "typed input diverged from this element's key" case,
matching `HintAdorner.OnInput`'s `_selectionLength = -1` early return).

Color mapping from WPF to WinUI 3, same accent-based palette. Verified directly against the
SDK's own `generic.xaml` (e.g. `AcrylicBrush.TintColor="{ThemeResource SystemAccentColorDark1}"`):
`SystemAccentColorLight2`/`SystemAccentColorDark1` are `Color` resources, not `Brush`
resources, so building the badge in code (not XAML) means looking each up as a `Color` and
wrapping it in `new SolidColorBrush(...)`. `TextOnAccentFillColorPrimaryBrush` is already a
full `SolidColorBrush` resource and is used directly.

| WPF (`HintAdorner`) | WinUI 3 | Resource kind |
|---|---|---|
| `SystemColors.AccentColorLight2Brush` (fill) | `SystemAccentColorLight2` | `Color` — wrap in `SolidColorBrush` |
| `SystemColors.AccentColorDark1Brush` (border) | `SystemAccentColorDark1` | `Color` — wrap in `SolidColorBrush` |
| `SystemColors.HighlightTextBrush` (text) | `TextOnAccentFillColorPrimaryBrush` | Already a `SolidColorBrush` — WinUI 3's standard brush for text drawn on an accent-filled surface, the semantic equivalent of "text that reads against a highlight/accent background" |

`SystemColors.AccentColorLight1Brush` (`_highlightTextBrush`) has no user in the final
`OnRender` — its only use, the substring-highlight draw call, is commented-out dead code
in the WPF original per the parent spec's "Explicit exclusions." It is not ported.

## Data flow

**On `AccessKeyDisplayRequested(sender, args)`:**

1. Look up an existing badge for `sender` in the layer's dictionary; if absent, create one
   (`new AccessKeyBadge(sender.AccessKey)`, add its `Visual` to the overlay `Canvas`,
   store it).
2. Compute this badge's position via `sender.TransformToVisual(overlayCanvas)` against
   `sender`'s bounds, and apply via `Canvas.SetLeft`/`SetTop` — recomputed on every call,
   not cached, so a badge stays correctly placed even if layout shifts mid-sequence.
3. If `args.PressedKeys` is a prefix of `sender.AccessKey` (including the empty-string
   case, right after Alt is pressed), call `UpdatePrefix(args.PressedKeys.Length)`.
   Otherwise call `UpdatePrefix(-1)` to hide it — this element's key no longer matches
   what has been typed.

**On `AccessKeyDisplayDismissed(sender, args)`:** remove `sender`'s badge, if any, from
both the dictionary and the `Canvas`. A dismiss for an element with no active badge (e.g.
dismissed twice) is a no-op, not an error.

**`ComboBox` chords need one extra `Watch` call, nothing more.** `DynamicAccessKeyScope.AttachSequentialKeys`
calls `badgeLayer.Watch(container)` at the point it assigns each `ComboBoxItem`'s key; once
registered, that item's events reach `AccessKeyBadgeLayer` exactly like any other hinted
element's — `AccessKeyBadgeLayer` itself does not need to know `ComboBox` exists.

## Error handling

- A dismiss with no matching badge: no-op (see above).
- `AreKeyTipsEnabled` accidentally left `true`: not a crash, but both the default and
  custom badge would render for the same element — guarded against with a comment at the
  startup call site, not runtime logic (there is nothing to defensively check; it is a
  single static assignment).
- No new failure modes are introduced in the underlying `AccessKeyManager` chord-matching
  or activation logic — this design only changes what is drawn, never whether or how a
  key sequence resolves.

## Testing

Matching the parent spec's own testing philosophy (framework behavior — chord matching,
scope ownership, display-mode transitions — is not re-tested; only new logic is), refined
against one fact checked during this design rather than assumed: this repo's WinUI 3 test
projects (`winui3/WinTabber.UI.Common.Tests` and siblings) have no existing test anywhere
that constructs a real `Window`/`XamlRoot` — every test so far is headless, pure-logic.
Constructing a live WinUI 3 window inside TUnit's plain console test host (no
`Application.Start`, no message loop) is unproven in this codebase, so this design does not
gate on it:

- **`AccessKeyBadge`'s prefix-to-underline-length mapping is pure string logic** (given an
  element's `AccessKey` and a `PressedKeys` value, does the badge show the full text
  underlined to the right length, or hide entirely) — extracted as an
  `internal static int ComputeUnderlineLength(string accessKey, string pressedKeys)`
  (-1 means hide), headless unit tests, no window required. This is the one piece of new
  logic with real branching to get wrong.
- **`AccessKeyBadgeLayer`'s create/position/remove-on-dismiss behavior against a real
  window is verified manually, live, in the running app** — the same fallback this
  migration's own plans use elsewhere when an automated desktop test has no established
  precedent to build on (e.g. the main migration plan's UI-Automation-with-a-screenshot-
  fallback pattern). `MediaControlsWindow` already exists and is already the live
  verification target the parent spec's own port was checked against. A concrete script
  for this is in the implementation plan's verification task, not hand-waved.
- No test targets `AccessKeyManager`/`AccessKeyInvoked` themselves, or anything in
  `DynamicAccessKeyScope` — both are unchanged by this work.

## Explicit exclusions

Everything the parent spec already excluded remains excluded: `GeneratedHintsProvider`,
`HintAdorner`'s commented-out substring-highlight draw call, `HintPosition`,
`HintChordState`, `HintActivationScope`, `IHintBehaviorKernel` and its two
implementations. This document adds no new exclusions beyond what "Deferred to a
follow-up task" in the parent spec already scoped out (nothing beyond the three numbered
items in Purpose, above).
