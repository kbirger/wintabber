# Access-Key Custom Badge Rendering (WinUI 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace WinUI 3's default access-key (key-tip) badge in `MediaControlsWindow`/`VolumeControls` with a custom badge matching the WPF app's `HintAdorner` look, including live typed-prefix underlining.

**Architecture:** Turn off the framework's default badges app-wide (`AccessKeyManager.AreKeyTipsEnabled = false`). A new `AccessKeyBadgeLayer` draws/positions/removes a small custom `AccessKeyBadge` (`Border`+`TextBlock`, built in code) into a `Canvas` overlay placed at a window's root. `AccessKeyDisplayRequested`/`AccessKeyDisplayDismissed` do **not** bubble (verified against metadata — see Global Constraints), so the layer's `Watch(element)` is called once per hinted element, not once per window. Positioning is computed fresh on every request via `TransformToVisual`. `MediaControlsWindow` is the only consumer today.

**Tech Stack:** WinUI 3 (`Microsoft.WindowsAppSDK` 1.8.251106002, per `Directory.Packages.props`), TUnit.

**Spec:** `docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md` — read this first; it documents the WinUI 3 API surface this plan relies on (verified against the installed package's own metadata, not assumed) and the reasoning behind every design choice below. Also relevant background (not modified by this plan): `docs/superpowers/specs/2026-09-17-hint-overlay-winui3-design.md` (the parent hint-overlay port this work completes) and `docs/superpowers/plans/2026-09-18-hint-overlay-winui3.md`.

## Global Constraints

- `AccessKeyManager.AreKeyTipsEnabled = false` must be set exactly once, app-wide, at startup — never per-window. Leave a comment at the call site: removing it would bring back the default badge drawn on top of the custom one.
- No exposed style/template exists for WinUI 3's default key-tip badge (verified against the SDK's own metadata) — this is why the badge is built as an independent visual, not a restyle of anything framework-provided.
- `AccessKeyDisplayRequested`/`AccessKeyDisplayDismissed` do **not** bubble. Verified: `AccessKeyInvokedEventArgs` (a sibling event, already used in `DynamicAccessKeyScope.cs`) has a `Handled` property — the standard signal for a routed, interceptable event. Neither `AccessKeyDisplayRequestedEventArgs` (only `PressedKeys`) nor `AccessKeyDisplayDismissedEventArgs` (no members at all) has one, and a worked example's own usage wires both handlers directly on the specific element, not at a container. **Every hinted element must be individually registered** with `AccessKeyBadgeLayer.Watch(element)` — there is no single subscription that reaches every hinted descendant of a window.
- `SystemAccentColorLight2`/`SystemAccentColorDark1` are `Color` resources, not `Brush` resources (verified against the SDK's own `generic.xaml`) — each must be looked up and wrapped in `new SolidColorBrush(...)` in code. `TextOnAccentFillColorPrimaryBrush` is already a `SolidColorBrush` and is used directly. Resource lookups use `TryGetValue` with a hardcoded fallback color, not a direct indexer, since a direct indexer throws if the key is ever absent from the merged resource dictionary at construction time and nothing above `AccessKeyBadge`'s constructor catches that.
- The prefix-underline must underline only the typed portion of the badge's text, leaving the remainder plain — `TextBlock.TextDecorations` alone cannot do this (it applies to the whole string), so the badge's text is built from two `Run`s inside `TextBlock.Inlines`, only the first one (the typed prefix) carrying `TextDecorations.Underline`.
- Real-world scope is unchanged from the parent spec: `MediaControlsWindow` and `VolumeControls` only. No other window is touched.
- No automated test in this codebase constructs a real WinUI 3 `Window`/`XamlRoot` (verified: `winui3/WinTabber.UI.Common.Tests` and its siblings are all headless today). This plan does not attempt to be the first — `AccessKeyBadgeLayer`'s live behavior is verified manually in the running app (Task 3), with only its pure logic (`AccessKeyBadge.ComputeUnderlineLength`) covered by an automated test.

---

## File Structure

**New files:**
- `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadge.cs` — the badge visual and its pure prefix-matching logic.
- `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs` — the event-wiring/positioning class, one instance per window.
- `winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeTests.cs` — unit tests for `AccessKeyBadge.ComputeUnderlineLength`.

**Modified files:**
- `winui3/WinTabberUI/App.xaml.cs` — `AreKeyTipsEnabled = false` at startup.
- `winui3/WinTabberUI/Views/MediaControlsWindow.xaml` — add the overlay `Canvas`; add `x:Name` to the prev/next buttons and both `VolumeControls` instances.
- `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs` — construct the `AccessKeyBadgeLayer`, `Watch` every static hinted element, register both `VolumeControls` instances.
- `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml` — add `x:Name` to the `Slider` and `ToggleButton`.
- `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs` — add `RegisterAccessKeyBadges(AccessKeyBadgeLayer)`.
- `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs` — `AttachSequentialKeys` takes the badge layer and calls `Watch` on each newly-realized `ComboBoxItem`.

---

### Task 1: `AccessKeyBadge` — the badge visual and its prefix logic

**Files:**
- Create: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadge.cs`
- Test: `winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeTests.cs`

**Interfaces:**
- Produces: `internal sealed class AccessKeyBadge` with `AccessKeyBadge(string text)`, `UIElement Visual { get; }`, `void UpdatePrefix(int underlineLength)` (`-1` hides the badge), and `internal static int ComputeUnderlineLength(string accessKey, string pressedKeys)` — Task 2 consumes all four members.

- [ ] **Step 1: Write the failing test**

```csharp
using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Common.Tests.AccessKeys;

public class AccessKeyBadgeTests
{
    [Test]
    public async Task ComputeUnderlineLength_ReturnsZero_WhenNoKeysPressedYet()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("BC", "");

        await Assert.That(length).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeUnderlineLength_ReturnsPressedLength_WhenPressedKeysIsAPrefix()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("BC", "B");

        await Assert.That(length).IsEqualTo(1);
    }

    [Test]
    public async Task ComputeUnderlineLength_ReturnsFullLength_WhenPressedKeysMatchesExactly()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("P", "P");

        await Assert.That(length).IsEqualTo(1);
    }

    [Test]
    public async Task ComputeUnderlineLength_ReturnsNegativeOne_WhenPressedKeysDoesNotMatch()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("P", "B");

        await Assert.That(length).IsEqualTo(-1);
    }

    [Test]
    public async Task ComputeUnderlineLength_IsCaseInsensitive()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("BC", "b");

        await Assert.That(length).IsEqualTo(1);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyBadgeTests/*"`
Expected: FAIL to compile — `AccessKeyBadge` does not exist yet.

- [ ] **Step 3: Write `AccessKeyBadge`**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// A custom access-key badge matching the WPF app's HintAdorner look (rounded rect, accent-color
/// fill, bold text, underlined typed prefix), since WinUI 3's default key-tip badge cannot be
/// restyled -- see docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md.
/// Built in code, not XAML, since instances are created and destroyed dynamically per active hint.
/// </summary>
internal sealed class AccessKeyBadge
{
    private const double PaddingX = 5;
    private const double PaddingY = 4;
    private const double CornerRadiusValue = 4;
    private const double FontSizeValue = 10;

    // Fallbacks only reached if the app-level resource dictionary is ever missing one of these
    // keys (should not happen in a running app -- every default control style already depends on
    // them -- but a direct indexer lookup throws on a miss with nothing above this constructor to
    // catch it, so TryGetResource below degrades to a plain, still-visible color instead).
    private static readonly Color FallbackFill = Color.FromArgb(255, 0, 120, 215);
    private static readonly Color FallbackBorder = Color.FromArgb(255, 0, 84, 153);

    private readonly Border _border;
    private readonly TextBlock _textBlock;
    private string _fullText = "";

    public AccessKeyBadge(string text)
    {
        _textBlock = new TextBlock
        {
            FontSize = FontSizeValue,
            FontWeight = FontWeights.Bold,
            Foreground = TryGetResource<Brush>("TextOnAccentFillColorPrimaryBrush")
                ?? new SolidColorBrush(Colors.White),
        };

        _border = new Border
        {
            CornerRadius = new CornerRadius(CornerRadiusValue),
            Padding = new Thickness(PaddingX, PaddingY, PaddingX, PaddingY),
            // SystemAccentColorLight2/Dark1 are Color resources, not Brush resources -- verified
            // against the SDK's own generic.xaml (e.g. AcrylicBrush.TintColor="{ThemeResource
            // SystemAccentColorDark1}"). Wrap each in a SolidColorBrush explicitly.
            Background = new SolidColorBrush(TryGetColorResource("SystemAccentColorLight2") ?? FallbackFill),
            BorderBrush = new SolidColorBrush(TryGetColorResource("SystemAccentColorDark1") ?? FallbackBorder),
            BorderThickness = new Thickness(0.5),
            IsHitTestVisible = false,
            Child = _textBlock,
        };

        _fullText = text;
        Render(0);
    }

    public UIElement Visual => _border;

    private static T? TryGetResource<T>(string key)
        where T : class
    {
        return Application.Current.Resources.TryGetValue(key, out var value) ? value as T : null;
    }

    // Color is a value type -- "as T" in TryGetResource<T> above only works for reference types,
    // so a boxed Color resource needs its own lookup rather than sharing that generic helper.
    private static Color? TryGetColorResource(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : null;
    }

    /// <summary>
    /// Redraws the badge's text with the first <paramref name="underlineLength"/> characters
    /// underlined -- the typed portion of the access key so far -- and the rest plain, matching
    /// HintAdorner.OnRender's SetTextDecorations(Underline, 0, _selectionLength). -1 hides the
    /// badge entirely (the typed input has diverged from this element's own key), matching
    /// HintAdorner.OnInput's _selectionLength = -1 early return.
    /// </summary>
    public void UpdatePrefix(int underlineLength)
    {
        if (underlineLength < 0)
        {
            _border.Visibility = Visibility.Collapsed;
            return;
        }

        _border.Visibility = Visibility.Visible;
        Render(underlineLength);
    }

    /// <summary>Redraws _textBlock's Inlines for the current _fullText, underlining the first
    /// underlineLength characters. Takes no text parameter and never assigns _fullText -- setting
    /// the text (constructor only) and rendering it (constructor and every later UpdatePrefix
    /// call) are kept as two separate jobs.</summary>
    private void Render(int underlineLength)
    {
        _textBlock.Inlines.Clear();

        if (underlineLength <= 0)
        {
            _textBlock.Inlines.Add(new Run { Text = _fullText });
            return;
        }

        // TextBlock.TextDecorations applies to the whole string -- there is no WinUI 3 equivalent
        // of WPF's FormattedText.SetTextDecorations(decorations, start, length) on a plain Text
        // property. Splitting into two Runs, only the first carrying the decoration, reproduces
        // the same partial-underline effect.
        _textBlock.Inlines.Add(new Run
        {
            Text = _fullText[..underlineLength],
            TextDecorations = TextDecorations.Underline,
        });
        if (underlineLength < _fullText.Length)
        {
            _textBlock.Inlines.Add(new Run { Text = _fullText[underlineLength..] });
        }
    }

    /// <summary>
    /// Pure logic, unit-tested independent of any visual: how much of <paramref name="accessKey"/>
    /// to underline given what has been typed so far, or -1 if <paramref name="pressedKeys"/> is
    /// not a prefix of it (matching HintAdorner.OnInput's HintText.StartsWith(currentInput) check).
    /// </summary>
    internal static int ComputeUnderlineLength(string accessKey, string pressedKeys)
    {
        return accessKey.StartsWith(pressedKeys, StringComparison.OrdinalIgnoreCase)
            ? pressedKeys.Length
            : -1;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyBadgeTests/*"`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadge.cs winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeTests.cs
git commit -m "feat: add custom access-key badge visual matching HintAdorner

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `AccessKeyBadgeLayer` — event wiring and positioning

**Files:**
- Create: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`

**Interfaces:**
- Consumes: `AccessKeyBadge` from Task 1 (`AccessKeyBadge(string)`, `.Visual`, `.UpdatePrefix(int)`, `AccessKeyBadge.ComputeUnderlineLength(string, string)`).
- Produces: `public sealed class AccessKeyBadgeLayer` with `public AccessKeyBadgeLayer(Canvas overlay)` and `public void Watch(UIElement element)` — Task 3 consumes both from `MediaControlsWindow.xaml.cs`, `VolumeControls.xaml.cs`, and (via a parameter added to `DynamicAccessKeyScope.AttachSequentialKeys`, also in Task 3) `DynamicAccessKeyScope.cs`.

`AccessKeyDisplayRequested`/`AccessKeyDisplayDismissed` do **not** bubble (see Global Constraints) — `AccessKeyBadgeLayer` has no "attach at a root" concept; every hinted element is registered individually via `Watch`. This task only adds the class; nothing calls `Watch` yet, so it has no other file to touch and no existing call site to break. `DynamicAccessKeyScope`'s own change is Task 3's job, folded in there together with the call sites it affects, so that task's diff is the complete, buildable set of changes needed to actually pass a layer around — see that task's own note on why.

This task has no automated test (see Global Constraints — no WinUI 3 test in this codebase constructs a real `Window`). Its deliverable is verified by compiling cleanly; its actual runtime behavior is verified live in Task 3, once real elements exist to watch.

- [ ] **Step 1: Write `AccessKeyBadgeLayer`**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Replaces WinUI 3's default key-tip badge with a custom AccessKeyBadge for every element
/// registered via Watch. AccessKeyDisplayRequested/Dismissed do NOT bubble -- confirmed against
/// metadata (AccessKeyInvokedEventArgs has a Handled property; these two do not) and a worked
/// example's own per-element wiring -- so there is no single attachment point that reaches every
/// hinted descendant the way WPF's HintBehavior had; each element needs its own Watch call.
/// Requires AccessKeyManager.AreKeyTipsEnabled = false to have been set (once, app-wide, at
/// startup) so the framework's own default badge does not also draw -- see
/// docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md.
/// </summary>
public sealed class AccessKeyBadgeLayer
{
    private readonly Canvas _overlay;
    private readonly Dictionary<UIElement, AccessKeyBadge> _badges = new();

    public AccessKeyBadgeLayer(Canvas overlay)
    {
        _overlay = overlay;
    }

    /// <summary>Registers one element to get a custom badge instead of the (disabled) default one.</summary>
    public void Watch(UIElement element)
    {
        element.AccessKeyDisplayRequested += OnAccessKeyDisplayRequested;
        element.AccessKeyDisplayDismissed += OnAccessKeyDisplayDismissed;
    }

    private void OnAccessKeyDisplayRequested(UIElement sender, AccessKeyDisplayRequestedEventArgs args)
    {
        var underlineLength = AccessKeyBadge.ComputeUnderlineLength(sender.AccessKey, args.PressedKeys);

        if (!_badges.TryGetValue(sender, out var badge))
        {
            if (underlineLength < 0)
            {
                // Never shown for this request (the very first character already diverges from
                // this element's key) -- nothing to create or hide.
                return;
            }

            badge = new AccessKeyBadge(sender.AccessKey);
            _badges[sender] = badge;
            _overlay.Children.Add(badge.Visual);
        }

        // Recomputed on every request, not cached, so the badge stays correctly placed even if
        // layout shifts mid-sequence. Matches HintPosition.TopLeft's exact offset (bounds.Left - 4,
        // bounds.Top - 4), ported from WPF's HintPosition.cs.
        var point = sender.TransformToVisual(_overlay).TransformPoint(new Point(0, 0));
        Canvas.SetLeft(badge.Visual, point.X - 4);
        Canvas.SetTop(badge.Visual, point.Y - 4);

        badge.UpdatePrefix(underlineLength);
    }

    private void OnAccessKeyDisplayDismissed(UIElement sender, AccessKeyDisplayDismissedEventArgs args)
    {
        // A dismiss for an element with no active badge (e.g. dismissed twice) is a no-op, not an
        // error -- Dictionary.Remove's bool return makes that the natural shape here.
        if (_badges.Remove(sender, out var badge))
        {
            _overlay.Children.Remove(badge.Visual);
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: Build succeeds with no errors — nothing calls `AccessKeyBadgeLayer` yet, so this is a clean, standalone addition.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs
git commit -m "feat: add AccessKeyBadgeLayer to wire custom badges into a window

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Wire into `MediaControlsWindow`/`VolumeControls`, disable default badges, verify live

**Files:**
- Modify: `winui3/WinTabberUI/App.xaml.cs`
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`
- Modify: `winui3/WinTabberUI/Views/MediaControlsWindow.xaml`
- Modify: `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs`
- Modify: `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml`
- Modify: `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs`

**Interfaces:**
- Consumes: `AccessKeyBadgeLayer(Canvas)`, `.Watch(UIElement)` from Task 2.
- Consumes: `Microsoft.UI.Xaml.Input.AccessKeyManager.AreKeyTipsEnabled` (framework API, verified in the spec).
- Produces: `DynamicAccessKeyScope.AttachSequentialKeys`'s new `AccessKeyBadgeLayer badgeLayer` parameter (Step 2) — consumed by this same task's three call-site rewrites (Step 6).
- Produces: `VolumeControls.RegisterAccessKeyBadges(AccessKeyBadgeLayer)` (Step 3) — consumed by `MediaControlsWindow.xaml.cs`'s constructor (Step 6).

`DynamicAccessKeyScope`'s change is folded into this task, not Task 2, so that every commit in this plan leaves the solution building — Task 2's `AccessKeyBadgeLayer` has no caller yet and needs none to compile; the moment something calls `AttachSequentialKeys` with the old two-parameter signature is also the moment this task rewrites that call site to the new one, in the same commit.

- [ ] **Step 1: Disable the default key-tip badge at startup**

In `winui3/WinTabberUI/App.xaml.cs`, add to the `using` block:

```csharp
using Microsoft.UI.Xaml.Input;
```

Replace the start of `OnLaunched`:

```csharp
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = Bootstrapper.Init();
```

with:

```csharp
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Must stay false: AccessKeyBadgeLayer (WinTabber.UI.Common.AccessKeys) draws its own
        // custom badge for every AccessKeyDisplayRequested. Leaving this true would draw the
        // framework's own default badge on top of it -- see
        // docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md.
        AccessKeyManager.AreKeyTipsEnabled = false;

        Services = Bootstrapper.Init();
```

- [ ] **Step 2: Give `DynamicAccessKeyScope.AttachSequentialKeys` a badge-layer parameter**

Replace the whole method (the file's only method) in `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`:

```csharp
    public static void AttachSequentialKeys(ComboBox owner, Action<ComboBoxItem, int> onActivated)
    {
        owner.IsAccessKeyScope = true;
        owner.ExitDisplayModeOnAccessKeyInvoked = false;

        var wired = new HashSet<ComboBoxItem>();
        owner.DropDownOpened += (_, _) =>
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is ComboBoxItem container)
                    {
                        container.AccessKey = (i + 1).ToString();
                        if (wired.Add(container))
                        {
                            container.AccessKeyInvoked += (_, args) =>
                            {
                                onActivated(container, owner.IndexFromContainer(container));
                                args.Handled = true;
                            };
                        }
                    }
                }

                AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
            });
        };
    }
```

with:

```csharp
    public static void AttachSequentialKeys(
        ComboBox owner,
        AccessKeyBadgeLayer badgeLayer,
        Action<ComboBoxItem, int> onActivated
    )
    {
        owner.IsAccessKeyScope = true;
        owner.ExitDisplayModeOnAccessKeyInvoked = false;

        var wired = new HashSet<ComboBoxItem>();
        owner.DropDownOpened += (_, _) =>
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is ComboBoxItem container)
                    {
                        container.AccessKey = (i + 1).ToString();
                        if (wired.Add(container))
                        {
                            // AccessKeyBadgeLayer only learns an element exists via Watch -- there
                            // is no bubbling to rely on here either, so a dynamically-realized
                            // ComboBoxItem needs the same explicit registration a static XAML
                            // element gets. Guarded by the same wired-dedup as AccessKeyInvoked
                            // just below it, for the same reason: DropDownOpened fires on every
                            // open and containers can be reused.
                            badgeLayer.Watch(container);
                            container.AccessKeyInvoked += (_, args) =>
                            {
                                onActivated(container, owner.IndexFromContainer(container));
                                args.Handled = true;
                            };
                        }
                    }
                }

                AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
            });
        };
    }
```

Also update the class's doc comment (directly above `AttachSequentialKeys`) to mention the new parameter — append one sentence: "`badgeLayer` gets the same `Watch` registration a static element would, since `AccessKeyDisplayRequested`/`Dismissed` do not bubble and a `ComboBoxItem` is otherwise invisible to it."

Do not build yet — this method's three existing call sites, in `MediaControlsWindow.xaml.cs`, are rewritten in Step 6, in the same commit as this change (Step 7 is the first build).

- [ ] **Step 3: Add `x:Name` to `VolumeControls`'s `Slider`/`ToggleButton`, and a registration method**

In `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml`, add `x:Name` to both controls. Replace:

```xml
        <Slider
            Minimum="0"
            Maximum="100"
            StepFrequency="5"
            IsEnabled="{Binding CanSetVolume}"
            AccessKey="{Binding VolumeHintText}"
            Value="{Binding Volume, Converter={StaticResource FloatToPercentageConverter}, Mode=TwoWay}"
        />
        <ToggleButton
            Style="{StaticResource MuteToggle}"
            Margin="7 0 0 0"
            Grid.Column="1"
            HorizontalAlignment="Center"
            VerticalAlignment="Stretch"
            AccessKey="{Binding MuteHintText}"
            IsChecked="{Binding IsMuted, Mode=OneWay}"
            Command="{Binding SetMuted}"
            CommandParameter="{Binding IsMuted, Converter={StaticResource InverseBoolConverter}}"
        />
```

with:

```xml
        <Slider
            x:Name="VolumeSlider"
            Minimum="0"
            Maximum="100"
            StepFrequency="5"
            IsEnabled="{Binding CanSetVolume}"
            AccessKey="{Binding VolumeHintText}"
            Value="{Binding Volume, Converter={StaticResource FloatToPercentageConverter}, Mode=TwoWay}"
        />
        <ToggleButton
            x:Name="MuteToggleButton"
            Style="{StaticResource MuteToggle}"
            Margin="7 0 0 0"
            Grid.Column="1"
            HorizontalAlignment="Center"
            VerticalAlignment="Stretch"
            AccessKey="{Binding MuteHintText}"
            IsChecked="{Binding IsMuted, Mode=OneWay}"
            Command="{Binding SetMuted}"
            CommandParameter="{Binding IsMuted, Converter={StaticResource InverseBoolConverter}}"
        />
```

In `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs`, add the `using` and method:

```csharp
using Microsoft.UI.Xaml.Controls;
using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Media.UserControls;

public sealed partial class VolumeControls : UserControl
{
    public VolumeControls()
    {
        InitializeComponent();
    }

    /// <summary>Registers this control's two hinted elements with a window's badge layer -- called
    /// once per instance from the owning window, since AccessKeyDisplayRequested/Dismissed do not
    /// bubble (see docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md),
    /// so the window cannot discover them on its own.</summary>
    public void RegisterAccessKeyBadges(AccessKeyBadgeLayer layer)
    {
        layer.Watch(VolumeSlider);
        layer.Watch(MuteToggleButton);
    }
}
```

- [ ] **Step 4: Add `x:Name` to the remaining hinted elements in `MediaControlsWindow.xaml`**

Add `x:Name` to the prev/next `Button`s and both `VolumeControls` instances. Replace:

```xml
            <uc:VolumeControls Grid.Column="1" DataContext="{Binding ActiveSession.SessionVolumeControls}" />
```

with:

```xml
            <uc:VolumeControls x:Name="SessionVolumeControls" Grid.Column="1" DataContext="{Binding ActiveSession.SessionVolumeControls}" />
```

Replace:

```xml
            <uc:VolumeControls Grid.Column="3" VerticalAlignment="Center" DataContext="{Binding ActiveSession.DeviceVolumeControls}" />
```

with:

```xml
            <uc:VolumeControls x:Name="DeviceVolumeControls" Grid.Column="3" VerticalAlignment="Center" DataContext="{Binding ActiveSession.DeviceVolumeControls}" />
```

Replace:

```xml
                <Button Style="{StaticResource MediaButton}" AccessKey="B" Command="{Binding ActiveSession.Playback.Prev}">
                    <FontIcon Glyph="&#xE892;" />
                </Button>
```

with:

```xml
                <Button x:Name="PrevButton" Style="{StaticResource MediaButton}" AccessKey="B" Command="{Binding ActiveSession.Playback.Prev}">
                    <FontIcon Glyph="&#xE892;" />
                </Button>
```

Replace:

```xml
                <Button Style="{StaticResource MediaButton}" AccessKey="F" Command="{Binding ActiveSession.Playback.Next}">
                    <FontIcon Glyph="&#xE893;" />
                </Button>
```

with:

```xml
                <Button x:Name="NextButton" Style="{StaticResource MediaButton}" AccessKey="F" Command="{Binding ActiveSession.Playback.Next}">
                    <FontIcon Glyph="&#xE893;" />
                </Button>
```

(`PlayPauseButton` already has an `x:Name`; the three `ComboBox`es already have `x:Name`s — `SessionSelector`, `PlaybackDeviceSelector`, `RecordingDeviceSelector`.)

- [ ] **Step 5: Add the overlay `Canvas` to `MediaControlsWindow.xaml`**

The root `Grid` (`RootGrid`) has two rows and two `Grid` children (one per row). Add the overlay as a third child, after both, so it is topmost (later XAML children render on top in a `Grid`). Replace the closing of `RootGrid` (the line right before `</winuiex:WindowEx>`):

```xml
        </Grid>
    </Grid>
</winuiex:WindowEx>
```

with:

```xml
        </Grid>

        <!-- Topmost: AccessKeyBadgeLayer (constructed in code-behind) draws custom access-key
             badges here. IsHitTestVisible=False so it never intercepts clicks meant for the real
             controls beneath it. Grid.RowSpan covers both of RootGrid's rows so a badge anywhere
             in the window can be positioned onto it. -->
        <Canvas x:Name="AccessKeyOverlay" Grid.RowSpan="2" IsHitTestVisible="False" />
    </Grid>
</winuiex:WindowEx>
```

- [ ] **Step 6: Construct the layer, watch every hinted element, in `MediaControlsWindow.xaml.cs`**

Replace the three existing `DynamicAccessKeyScope.AttachSequentialKeys` calls at the end of the constructor:

```csharp
        DynamicAccessKeyScope.AttachSequentialKeys(SessionSelector, (_, index) =>
        {
            SessionSelector.SelectedIndex = index;
            SessionSelector.IsDropDownOpen = false;
        });
        DynamicAccessKeyScope.AttachSequentialKeys(PlaybackDeviceSelector, (_, index) =>
        {
            PlaybackDeviceSelector.SelectedIndex = index;
            PlaybackDeviceSelector.IsDropDownOpen = false;
        });
        DynamicAccessKeyScope.AttachSequentialKeys(RecordingDeviceSelector, (_, index) =>
        {
            RecordingDeviceSelector.SelectedIndex = index;
            RecordingDeviceSelector.IsDropDownOpen = false;
        });
    }
```

with:

```csharp
        var accessKeyBadgeLayer = new AccessKeyBadgeLayer(AccessKeyOverlay);
        accessKeyBadgeLayer.Watch(SessionSelector);
        accessKeyBadgeLayer.Watch(PlaybackDeviceSelector);
        accessKeyBadgeLayer.Watch(RecordingDeviceSelector);
        accessKeyBadgeLayer.Watch(PrevButton);
        accessKeyBadgeLayer.Watch(PlayPauseButton);
        accessKeyBadgeLayer.Watch(NextButton);
        SessionVolumeControls.RegisterAccessKeyBadges(accessKeyBadgeLayer);
        DeviceVolumeControls.RegisterAccessKeyBadges(accessKeyBadgeLayer);

        DynamicAccessKeyScope.AttachSequentialKeys(SessionSelector, accessKeyBadgeLayer, (_, index) =>
        {
            SessionSelector.SelectedIndex = index;
            SessionSelector.IsDropDownOpen = false;
        });
        DynamicAccessKeyScope.AttachSequentialKeys(PlaybackDeviceSelector, accessKeyBadgeLayer, (_, index) =>
        {
            PlaybackDeviceSelector.SelectedIndex = index;
            PlaybackDeviceSelector.IsDropDownOpen = false;
        });
        DynamicAccessKeyScope.AttachSequentialKeys(RecordingDeviceSelector, accessKeyBadgeLayer, (_, index) =>
        {
            RecordingDeviceSelector.SelectedIndex = index;
            RecordingDeviceSelector.IsDropDownOpen = false;
        });
    }
```

(`using WinTabber.UI.Common.AccessKeys;` is already present in this file's `using` block.)

- [ ] **Step 7: Build**

Run: `dotnet build WinTabber.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 8: Run the full test suite to check for regressions**

Run: `dotnet test --solution WinTabber.slnx`
Expected: PASS across all test projects, including the 5 new `AccessKeyBadgeTests`.

- [ ] **Step 9: Live verification in the running app**

This is the real proof for `AccessKeyBadgeLayer` (see Global Constraints — no automated test covers this). Launch the WinUI 3 app (`dotnet run --project winui3/WinTabberUI/WinTabberUI.csproj`, or however this migration's other live-verification steps launch it — check `docs/superpowers/plans/2026-09-12-wpf-to-winui3-migration.md`'s established method if unsure), open `MediaControlsWindow` via its real hotkey, and confirm:

1. Pressing Alt shows a custom badge (rounded rect, accent-color fill, bold text) over every control that has an `AccessKey` — the three `ComboBox`es ("A"/"S"/"R"), the play/pause/prev/next controls ("P"/"B"/"F"), and the volume/mute controls in each `VolumeControls` instance — **not** the framework's plain default badge.
2. No badge appears anywhere that isn't a real hinted control (confirming `AreKeyTipsEnabled = false` didn't leave any stray default badge behind, and that every hinted element in this window really did get a `Watch` call — a missed one shows no badge at all for that control, which is exactly the failure mode this step exists to catch).
3. Every `VolumeControls` badge shows real text ("V"/"M" or whatever `VolumeHintText`/`MuteHintText` actually resolve to at runtime), not an empty rounded rect — `ComputeUnderlineLength("", "")` returns `0`, not `-1`, so an empty/null `AccessKey` binding would silently produce a visible-but-blank badge rather than no badge at all, which check 2 alone would not catch.
4. Typing part of a multi-character key underlines the typed prefix on every badge still matching, and hides every badge that no longer matches (e.g. if any control's key shares a prefix with another's).
5. Opening a `ComboBox`'s drop-down while its own badge is showing (continuing the `DynamicAccessKeyScope` chord) shows numbered badges on its items, styled the same as the top-level badges — proving `AttachSequentialKeys`'s new `Watch` call actually reaches dynamically-keyed `ComboBoxItem`s.
6. Completing a key sequence, or pressing Escape mid-sequence, removes every visible badge (no leftover badge stuck on screen).
7. Re-opening the same `ComboBox`'s drop-down a second time in the same session still shows correctly-drawn item badges — this is the "Known accepted gap" the parent spec accepted (default badges stopped redrawing after a `ComboBox`'s first open); confirming it here proves custom rendering incidentally fixed it, per the spec's Purpose section.

If any of these 7 checks fails, treat it as a real bug in this plan's implementation and fix it before considering this task done — do not defer any of them.

- [ ] **Step 10: Commit**

```bash
git add winui3/WinTabberUI/App.xaml.cs winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs \
        winui3/WinTabberUI/Views/MediaControlsWindow.xaml winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs \
        winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs
git commit -m "feat: wire custom access-key badges into MediaControlsWindow/VolumeControls

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Self-Review Notes

- **Spec coverage:** replace default badge with a `HintAdorner`-styled visual (Task 1) — done; position via `TransformToVisual` (Task 2) — done, using the exact `HintPosition.TopLeft` offset ported from the WPF original; live prefix-underline via `PressedKeys` (Task 1's `ComputeUnderlineLength` + Task 2's per-request call) — done; incidentally resolve the default-badge-redraw gap (Task 3, Step 9 check 7) — done, verified explicitly rather than assumed; `AreKeyTipsEnabled = false` exactly once, app-wide (Task 3, Step 1) — done; every hinted element individually registered via `Watch`, since the display events don't bubble (Task 3, Steps 2-6) — done, covering both `VolumeControls` instances, the prev/play-pause/next buttons, all three `ComboBox`es, and (via `DynamicAccessKeyScope`'s updated signature, Task 3 Step 2) every dynamically-keyed `ComboBoxItem`.
- **Placeholder scan:** no TBDs. Step 9's "however it launches" hedge is the one place phrased as a check rather than a fixed command, because the exact launch command may have changed since this plan was written — it tells the implementer exactly what document to check rather than guessing; every actual verification criterion in that step is concrete and enumerated.
- **Type consistency:** `AccessKeyBadge`'s constructor, `Visual`, `UpdatePrefix`, and `ComputeUnderlineLength` signatures in Task 1 match exactly what Task 2's `AccessKeyBadgeLayer` calls. `AccessKeyBadgeLayer`'s constructor and `Watch(UIElement)` in Task 2 match every call site added in Task 3 (`MediaControlsWindow.xaml.cs`'s six direct `Watch` calls, `VolumeControls.RegisterAccessKeyBadges`'s two, and `DynamicAccessKeyScope.AttachSequentialKeys`'s one per realized `ComboBoxItem`). `DynamicAccessKeyScope.AttachSequentialKeys`'s new three-parameter signature (Task 3 Step 2) matches all three call sites Task 3 Step 6 rewrites.
- **A note on why `DynamicAccessKeyScope` moved into Task 3:** an earlier draft of this plan put that change in Task 2, deliberately leaving the solution non-building until Task 3 fixed the call sites. Corrected during review: every implementer in this plan is expected to run the full test suite before committing, so a task that can't build would fail that step for no real reason. `AccessKeyBadgeLayer` (Task 2) has no caller and needs none to compile; `DynamicAccessKeyScope`'s signature change and its two call-site rewrites are one atomic, always-buildable unit, so they now live together in Task 3.
