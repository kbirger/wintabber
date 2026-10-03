# Access-Key Badge Popup Z-Order Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make item access-key badges for a `ComboBox`'s drop-down draw above the drop-down, using a mechanism any window or control can reuse without editing a `ControlTemplate`.

**Architecture:** `AccessKeyBadgeLayer` currently gives every badge its own `Popup`. That fails for drop-down items: a `ComboBox` drop-down is itself a light-dismiss `Popup`, and WinUI keeps light-dismiss popups on a stacking layer above ordinary popups regardless of open order, so a plain sibling `Popup` can never draw above it (confirmed live: `VisualTreeHelper.GetOpenPopupsForXamlRoot` lists the drop-down's `Popup` ahead of badge popups opened earlier). The fix: when a popup-owning control (a `ComboBox` today) is about to show its own popup, `AccessKeyBadgeLayer` snapshots the currently-open popups, lets the drop-down open, diffs the new snapshot to find the one new `Popup`, and adds each item badge's `Visual` directly as a child of that popup's own content panel instead of wrapping it in a new sibling `Popup`. This uses only the framework's already-open popup and its already-existing content panel — no `ControlTemplate` edit, no XAML changes, portable to any future popup-owning control by calling the same method.

**Tech Stack:** WinUI 3 (Windows App SDK 1.8), C#, TUnit.

**Spec:** `docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md` (original badge design — this plan amends its `AccessKeyBadgeLayer`/`DynamicAccessKeyScope` sections for drop-down items only; static, non-popup badges are unchanged).

## Global Constraints

- Static (non-drop-down) badges keep using `AccessKeyBadgeLayer`'s existing per-badge `Popup` — do not change that path.
- No `ControlTemplate`, `Style`, or other XAML edits for `ComboBox` or `ComboBoxItem`. The fix is code-only.
- `DynamicAccessKeyScope.AttachSequentialKeys(ComboBox owner, AccessKeyBadgeLayer badgeLayer, Action<ComboBoxItem, int> onActivated)`'s public signature does not change — existing call sites in `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs` keep compiling unmodified.
- Remove all `// TEMP-DIAG` code and the `Diag` method added during live debugging before this plan's final commit. Convert `AccessKeyBadgeLayer.cs` back to LF line endings (it was accidentally saved as CRLF; `git diff` against `HEAD` must show only real changes, not whitespace/line-ending noise).

---

### Task 1: Popup-diff helper on `AccessKeyBadgeLayer`

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`
- Test: `winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeLayerTests.cs` (new file — no UI-thread popup available in a unit test, so this task tests only the pure diff logic, extracted as a static method)

**Interfaces:**
- Consumes: nothing new.
- Produces: `internal static Popup? FindNewPopup(IReadOnlyList<Popup> before, IReadOnlyList<Popup> after)` — a pure, static, testable method. Task 2 calls it from inside `AccessKeyBadgeLayer`.

The diff itself (comparing two popup lists to find the one absent from `before`) is the only part of this mechanism that has no dependency on a live `XamlRoot`, a real `ComboBox`, or a real `Popup` — so it is the only part worth a fast, no-window unit test. Everything else in this plan runs against the live app, the same way the rest of the access-key work was verified.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.UI.Xaml.Controls.Primitives;
using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Common.Tests.AccessKeys;

public class AccessKeyBadgeLayerTests
{
    [Test]
    public void FindNewPopup_returns_the_one_popup_absent_from_before()
    {
        var p1 = new Popup();
        var p2 = new Popup();
        var p3 = new Popup();

        var before = new[] { p1, p2 };
        var after = new[] { p1, p2, p3 };

        var result = AccessKeyBadgeLayer.FindNewPopup(before, after);

        Assert.That(result, Is.SameAs(p3));
    }

    [Test]
    public void FindNewPopup_returns_null_when_nothing_new()
    {
        var p1 = new Popup();
        var before = new[] { p1 };
        var after = new[] { p1 };

        var result = AccessKeyBadgeLayer.FindNewPopup(before, after);

        Assert.That(result, Is.Null);
    }
}
```

Note: `Popup` is a `Microsoft.UI.Xaml.Controls.Primitives.DependencyObject`-derived type. Constructing one requires no `XamlRoot` and no window, so this runs headless like the rest of `WinTabber.UI.Common.Tests`. If the test project's TUnit/assertion setup uses a different assertion syntax than `Assert.That`, match whatever `AccessKeyBadgeTests.cs` already uses in the same directory — read that file first and mirror its assertion style exactly.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyBadgeLayerTests/*"`
Expected: FAIL — `AccessKeyBadgeLayer.FindNewPopup` does not exist yet (compile error).

- [ ] **Step 3: Write minimal implementation**

Add to `AccessKeyBadgeLayer.cs`, inside the `AccessKeyBadgeLayer` class:

```csharp
/// <summary>
/// Finds the one Popup present in <paramref name="after"/> but not in <paramref name="before"/> --
/// used to identify a ComboBox's own drop-down Popup right after it opens, by diffing the set of
/// open popups just before and just after DropDownOpened fires. Returns null if nothing new opened
/// (should not happen when called right after DropDownOpened, but is not this method's job to
/// assert -- the caller decides what a null means for it).
/// </summary>
internal static Popup? FindNewPopup(IReadOnlyList<Popup> before, IReadOnlyList<Popup> after)
{
    foreach (var popup in after)
    {
        if (!before.Contains(popup))
        {
            return popup;
        }
    }

    return null;
}
```

Add `using Microsoft.UI.Xaml.Controls.Primitives;` at the top if not already present (Task 2 also needs it for `Popup`, which the current file does not yet reference outside this addition).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyBadgeLayerTests/*"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeLayerTests.cs
git commit -m "test: add FindNewPopup diff helper to AccessKeyBadgeLayer"
```

---

### Task 2: `WatchPopupOwner` — inject item badges into the owner's own popup

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`

**Interfaces:**
- Consumes: `internal static Popup? FindNewPopup(...)` (Task 1).
- Produces: `public void WatchPopupOwner(UIElement owner, Action popupOwnerOpening, Action<Panel> onPopupOpened)` — Task 3 (`DynamicAccessKeyScope`) calls this once per `ComboBox` in place of the plain `Watch` calls it currently does for items.

  Exact contract: the caller passes `popupOwnerOpening`, a callback that performs whatever action opens the owner's popup (for a `ComboBox` this is nothing — the drop-down is already open by the time `DropDownOpened` fires — so `DynamicAccessKeyScope` will pass a no-op `() => { }` here; the parameter exists so the method's own snapshot-diff timing is correct for a popup that opens asynchronously in a future caller, without forcing every caller to already be past that point). `onPopupOpened` receives the new popup's content `Panel` so the caller can add badge visuals to it directly with `Canvas.SetLeft`/`Canvas.SetTop` (or `Panel.Children.Add` for a non-`Canvas` panel — see the null/type-mismatch handling below).

- [ ] **Step 1: Add `WatchPopupOwner` to `AccessKeyBadgeLayer`**

```csharp
/// <summary>
/// Locates a popup-owning control's own drop-down/flyout Popup right after it opens, by
/// snapshotting the open popups just before <paramref name="popupOwnerOpening"/> runs and diffing
/// against the open popups right after. Hands the caller that popup's content Panel via
/// <paramref name="onPopupOpened"/> so item badges can be added as direct children of it -- sharing
/// the SAME popup as the drop-down, not a sibling one -- which is required for them to draw above
/// the drop-down's own content: a ComboBox drop-down is a light-dismiss Popup, and WinUI keeps
/// light-dismiss popups on a stacking layer above ordinary Popups regardless of open order, so no
/// sibling Popup (however recently opened) can ever draw above it. Confirmed live via
/// VisualTreeHelper.GetOpenPopupsForXamlRoot: the drop-down's Popup was listed ahead of badge
/// Popups opened strictly earlier.
///
/// Does nothing (onPopupOpened is not called) if no new Popup is found, or if the new Popup's
/// Child is not a Panel -- both are silent no-ops, not errors: a badge that fails to place itself
/// is a visible-but-recoverable gap, not a crash, and future Windows App SDK versions could change
/// the drop-down's internal Child type without warning (see this method's caller-facing risk note
/// in the plan/spec).
/// </summary>
public void WatchPopupOwner(UIElement owner, Action popupOwnerOpening, Action<Panel> onPopupOpened)
{
    var before = VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();

    popupOwnerOpening();

    var after = VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();
    var newPopup = FindNewPopup(before, after);

    if (newPopup?.Child is Panel panel)
    {
        onPopupOpened(panel);
    }
}
```

Add `using Microsoft.UI.Xaml.Media;` (for `VisualTreeHelper`) and `using System.Linq;` at the top of the file if not already present.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: 0 errors. `WatchPopupOwner` has no caller yet, which is fine — nothing in this task exercises it.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs
git commit -m "feat: add WatchPopupOwner to inject badges into an owner's own popup"
```

---

### Task 3: Route `ComboBox` item badges through `WatchPopupOwner`

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Consumes: `AccessKeyBadgeLayer.WatchPopupOwner(UIElement owner, Action popupOwnerOpening, Action<Panel> onPopupOpened)` (Task 2).
- Produces: no change to `AttachSequentialKeys`'s own signature — `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs`'s three call sites need no edits.

Today, `AttachSequentialKeys` calls `badgeLayer.Watch(container)` for each `ComboBoxItem`, which gives that item its own sibling `Popup` (the mechanism that loses to the drop-down's light-dismiss layer). Replace that per-item `Watch` with one `WatchPopupOwner` call per `DropDownOpened`, and have each item badge add itself to the returned panel directly instead of going through the normal `OnAccessKeyDisplayRequested` flow. This means item badges are placed by `DynamicAccessKeyScope` itself, not by `AccessKeyBadgeLayer`'s own event-driven path — the item's `AccessKeyDisplayRequested`/`Dismissed` events still exist on the framework side, but this plan does not wire them for items (see Task 4 for the position/visibility consequence this has, and the follow-up it flags).

- [ ] **Step 1: Rewrite the `DropDownOpened` handler**

Replace the full body of `DynamicAccessKeyScope.cs` with:

```csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Assigns a sequential digit AccessKey ("1", "2", "3", ...) to each item container of a
/// ComboBox's drop-down as it is realized, and routes activation through onActivated. ComboBox
/// has no ContainerContentChanging (confirmed unavailable on this type in the Windows App SDK),
/// so containers are only reachable via ContainerFromIndex once DropDownOpened fires.
/// </summary>
public static class DynamicAccessKeyScope
{
    /// <summary>
    /// ExitDisplayModeOnAccessKeyInvoked=false plus the explicit EnterDisplayMode call keep the
    /// access-key badge/chord session continuous across the transition from the ComboBox's own
    /// key into its items' keys -- without both, the user must press Alt a second time after the
    /// drop-down opens. The HashSet dedup is required because DropDownOpened fires on every open
    /// and containers can be reused; without it, badges would be added to the drop-down panel
    /// again on every re-open, duplicating them. Item badges are added directly into the
    /// drop-down's own Popup panel via badgeLayer.WatchPopupOwner, not through badgeLayer.Watch --
    /// a ComboBox drop-down is a light-dismiss Popup and always draws above a badge's own sibling
    /// Popup regardless of open order, so an item badge must live inside the SAME Popup as the
    /// drop-down to be visible over it.
    /// </summary>
    public static void AttachSequentialKeys(
        ComboBox owner,
        AccessKeyBadgeLayer badgeLayer,
        Action<ComboBoxItem, int> onActivated
    )
    {
        owner.IsAccessKeyScope = true;
        owner.ExitDisplayModeOnAccessKeyInvoked = false;

        var wired = new HashSet<ComboBoxItem>();

        // Set only when the owner's own access key opened the drop-down, so a mouse or arrow-key
        // open does not start an access-key display session the user never asked for.
        var openedByAccessKey = false;
        owner.AccessKeyInvoked += (_, _) => openedByAccessKey = true;

        owner.DropDownOpened += (_, _) =>
        {
            var continueChord = openedByAccessKey;
            openedByAccessKey = false;

            owner.DispatcherQueue.TryEnqueue(() =>
            {
                // The drop-down's Popup is already open by the time DropDownOpened fires, so the
                // "opening" action WatchPopupOwner performs between its before/after snapshots is a
                // no-op here -- the snapshot-before-open ordering only matters for a popup owner
                // that opens asynchronously after this call, which a ComboBox is not.
                badgeLayer.WatchPopupOwner(
                    owner,
                    popupOwnerOpening: () => { },
                    onPopupOpened: panel =>
                    {
                        for (int i = 0; i < owner.Items.Count; i++)
                        {
                            if (owner.ContainerFromIndex(i) is not ComboBoxItem container)
                            {
                                continue;
                            }

                            container.AccessKey = (i + 1).ToString();

                            if (!wired.Add(container))
                            {
                                continue;
                            }

                            container.AccessKeyInvoked += (_, args) =>
                            {
                                onActivated(container, owner.IndexFromContainer(container));
                                args.Handled = true;
                            };

                            AccessKeyBadgeLayer.AddItemBadge(panel, container);
                        }
                    }
                );

                if (continueChord)
                {
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
            });
        };
    }
}
```

Note: this step references `AccessKeyBadgeLayer.AddItemBadge`, which does not exist yet — Task 4 adds it. This task will not compile on its own; Steps 2–3 below happen together with Task 4, not before it. (This is the one place in this plan where two tasks are coupled tightly enough that splitting them would leave an intermediate commit that does not build — acceptable here because Task 4 is small and immediately follows.)

- [ ] **Step 2: Proceed directly to Task 4** — do not attempt to build or commit this task in isolation.

---

### Task 4: `AddItemBadge` — place and keep one item's badge live in the drop-down panel

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`

**Interfaces:**
- Consumes: nothing new from other tasks.
- Produces: `public static void AddItemBadge(Panel dropDownPanel, ComboBoxItem container)` — consumed by Task 3's `DynamicAccessKeyScope.AttachSequentialKeys`.

`AccessKeyBadgeLayer`'s normal per-element flow (`Watch` → `OnAccessKeyDisplayRequested`/`Dismissed`) positions a badge using `TransformToVisual(null)` — coordinates relative to the window. A badge placed inside the drop-down's own panel instead needs coordinates relative to *that panel*, and needs to react to the container's own `AccessKeyDisplayRequested`/`Dismissed` events (typed input still narrows/hides the badge exactly as it does for static badges) without going through the shared `_badges` dictionary keyed for the sibling-`Popup` path, since this badge is not in a `Popup` of its own.

- [ ] **Step 1: Add `AddItemBadge`**

```csharp
/// <summary>
/// Creates one AccessKeyBadge for a dynamically-keyed ComboBoxItem and adds it as a direct child
/// of dropDownPanel -- the drop-down's own Popup content, located by WatchPopupOwner -- rather than
/// through the normal Watch/_badges path, which places a badge in its own sibling Popup that a
/// ComboBox drop-down (a light-dismiss Popup) always draws above regardless of open order. Wires
/// the container's own AccessKeyDisplayRequested/Dismissed directly, bypassing
/// OnAccessKeyDisplayRequested/Dismissed entirely, since those methods assume the sibling-Popup
/// shape this badge does not use.
/// </summary>
public static void AddItemBadge(Panel dropDownPanel, ComboBoxItem container)
{
    var badge = new AccessKeyBadge(container.AccessKey);
    dropDownPanel.Children.Add(badge.Visual);

    container.AccessKeyDisplayRequested += (sender, args) =>
    {
        var underlineLength = AccessKeyBadge.ComputeUnderlineLength(sender.AccessKey, args.PressedKeys);

        // Position is relative to dropDownPanel, not the window -- container and dropDownPanel
        // share that panel as a common ancestor once the drop-down is open, so
        // TransformToVisual(dropDownPanel) gives coordinates already in the right space, matching
        // the -4/-4 offset the window-level path uses (see OnAccessKeyDisplayRequested).
        var point = container.TransformToVisual(dropDownPanel).TransformPoint(new Point(0, 0));
        Canvas.SetLeft(badge.Visual, point.X - 4);
        Canvas.SetTop(badge.Visual, point.Y - 4);

        badge.UpdatePrefix(underlineLength);
    };

    container.AccessKeyDisplayDismissed += (_, _) =>
    {
        badge.UpdatePrefix(-1);
    };
}
```

Note: `Canvas.SetLeft`/`Canvas.SetTop` are no-ops on a non-`Canvas` `Panel` (they set attached properties `Canvas` reads, which a `StackPanel`/`Grid` ignores). The drop-down panel observed live was a `Canvas`, matching the `Panel` type check `WatchPopupOwner` already applies. If a future SDK version's drop-down uses a different `Panel` subtype, badges silently render at that panel's default layout position instead of over their item — a visible-but-recoverable degradation, consistent with `WatchPopupOwner`'s own no-op-on-mismatch behavior. Add `using Windows.Foundation;` (for `Point`) if not already present.

- [ ] **Step 2: Build the modified files together (Tasks 3 and 4)**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: 0 errors.

- [ ] **Step 3: Run the existing badge unit tests to confirm no regression**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyBadgeTests/*"`
Expected: PASS (all previously-passing tests still pass — `ComputeUnderlineLength` is unchanged).

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "fix: draw ComboBox item access-key badges inside the drop-down's own popup"
```

---

### Task 5: Restore the static-badge per-badge Popup z-order fix

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`

**Interfaces:**
- Consumes: nothing new from Tasks 1-4.
- Produces: no change to `Watch(UIElement element)`'s public signature, or to `WatchPopupOwner`/`AddItemBadge`/`FindNewPopup` (Tasks 1-4) — this task only changes how a static badge's own visual is hosted.

**Why this task exists:** earlier in the same development session that produced this plan (before this plan existed), the original bug — access-key badges drawing behind an open `ComboBox` drop-down — was live-diagnosed and fixed for *static* (non-drop-down-item) badges: each badge was given its own `Popup` instead of being a plain `Canvas` child, because a `Canvas` child can never draw above any `Popup` (the drop-down included). That fix was verified live with the user and never regressed since — until Task 1 of this plan edited `AccessKeyBadgeLayer.cs` without knowing the file's then-uncommitted state carried that fix, and committed a version that reverts to the original `Canvas`-child mechanism. This task re-applies it, now permanently, as a reviewed and committed task like the rest of this plan.

This task's mechanism is a plain per-element `Popup`, unrelated to `WatchPopupOwner`'s owner-popup-injection mechanism (Tasks 2-4) — those two techniques solve two different placements (item badges must share the drop-down's own popup; static badges just need to draw above whatever else is open, which their own `Popup` alone achieves, since a `Popup` opened later draws above a `Popup` opened earlier *unless* the other one is light-dismiss — a static badge never competes with a light-dismiss popup unless the user opens a `ComboBox` drop-down while a *different* static badge is showing, which is exactly the original bug and exactly what a `Popup` fixes for a non-light-dismiss competitor).

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs` in full before editing — it now contains `FindNewPopup`, `WatchPopupOwner`, and `AddItemBadge` from Tasks 1, 2, and 4, none of which this task touches. Only the constructor, `_badges` field, `Watch`, `OnAccessKeyDisplayRequested`, and `OnAccessKeyDisplayDismissed` change.

- [ ] **Step 2: Replace the static-badge storage and both event handlers**

Replace the `_badges` field's declared type, the constructor stays the same, and replace both handler methods:

```csharp
    private readonly Canvas _overlay;
    private readonly Dictionary<UIElement, (AccessKeyBadge Badge, Popup Popup)> _badges = new();

    // Each static badge lives in its own Popup, not as a child of the overlay Canvas. A ComboBox
    // drop-down is itself a Popup, and WinUI draws every open Popup in the PopupRoot above all
    // normal window content, so a Canvas child can never draw above it -- confirmed live: this
    // Canvas-child version was the original form of this bug (badges drawing behind an open
    // drop-down). The overlay Canvas stays only as the anchor supplying XamlRoot.
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

        if (_badges.TryGetValue(sender, out var existing) && existing.Badge.Text != sender.AccessKey)
        {
            // sender's AccessKey changed since this badge was created (DynamicAccessKeyScope
            // reassigns a reused ComboBoxItem's key on every DropDownOpened) -- the cached badge's
            // text is now stale and must not be reused with an underline length computed for a
            // different string.
            existing.Popup.IsOpen = false;
            _badges.Remove(sender);
        }

        if (!_badges.TryGetValue(sender, out var entry))
        {
            if (underlineLength < 0)
            {
                // Never shown for this request (the very first character already diverges from
                // this element's key) -- nothing to create or hide.
                return;
            }

            var newBadge = new AccessKeyBadge(sender.AccessKey);
            var popup = new Popup
            {
                XamlRoot = _overlay.XamlRoot,
                Child = newBadge.Visual,
                IsHitTestVisible = false,
                ShouldConstrainToRootBounds = false,
            };
            entry = (newBadge, popup);
            _badges[sender] = entry;
        }

        // Recomputed on every request, not cached, so the badge stays correctly placed even if
        // layout shifts mid-sequence. Matches HintPosition.TopLeft's exact offset (bounds.Left - 4,
        // bounds.Top - 4), ported from WPF's HintPosition.cs. A Popup with a XamlRoot and no parent
        // is positioned in window-content coordinates, which TransformToVisual(null) yields.
        var point = sender.TransformToVisual(null).TransformPoint(new Point(0, 0));
        entry.Popup.HorizontalOffset = point.X - 4;
        entry.Popup.VerticalOffset = point.Y - 4;
        entry.Popup.IsOpen = true;

        entry.Badge.UpdatePrefix(underlineLength);
    }

    private void OnAccessKeyDisplayDismissed(UIElement sender, AccessKeyDisplayDismissedEventArgs args)
    {
        // A dismiss for an element with no active badge (e.g. dismissed twice) is a no-op, not an
        // error -- Dictionary.Remove's bool return makes that the natural shape here.
        if (_badges.Remove(sender, out var entry))
        {
            entry.Popup.IsOpen = false;
        }
    }
```

Add `using Microsoft.UI.Xaml.Controls.Primitives;` (for `Popup`) if not already present — `WatchPopupOwner` (Task 2) already needs `Popup` too, so this `using` may already be there; check before adding a duplicate.

- [ ] **Step 3: Build and run the existing badge unit tests**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: 0 errors.

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyBadgeTests/*"`
Expected: PASS, no regressions (`ComputeUnderlineLength` is untouched by this task).

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyBadgeLayerTests/*"`
Expected: PASS — `FindNewPopup<T>` (Task 1) is untouched by this task.

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs
git commit -m "fix: restore per-badge Popup for static access-key badges, lost in an earlier task's edit"
```

---

### Task 6: Fix WatchPopupOwner's snapshot timing for a framework-driven popup open

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Consumes: `internal static T? FindNewPopup<T>(...)` (Task 1) — unchanged, still used internally.
- Produces: `public IReadOnlyList<Popup> SnapshotOpenPopups(UIElement owner)` and `public void InjectIntoNewPopup(IReadOnlyList<Popup> before, Action<Panel> onPopupOpened)`, **replacing** `WatchPopupOwner` (Task 2) entirely — `WatchPopupOwner` is deleted, not kept alongside these.

**Why this task exists:** live testing after Task 5 found item badges never appear and digit-key activation never works — confirmed by code inspection, not guesswork. `WatchPopupOwner(owner, popupOwnerOpening, onPopupOpened)` snapshots open popups, runs `popupOwnerOpening`, then snapshots again to find what's new. `DynamicAccessKeyScope` calls it from inside `owner.DropDownOpened`, passing `popupOwnerOpening: () => { }` — but `DropDownOpened` fires *after* the drop-down's own `Popup` has already opened. Both snapshots taken inside `WatchPopupOwner` therefore see the identical popup list, `FindNewPopup` always returns null, and `onPopupOpened` (which adds every item's badge and wires its `AccessKeyInvoked`) never runs. `WatchPopupOwner`'s single-call, synchronous-bracketing design cannot work for any popup that the framework opens asynchronously, outside our control — only for a popup our own code opens synchronously between the two snapshots. A `ComboBox` drop-down is the former.

**The fix:** split the one method into two, callable from two different moments. The "before" snapshot must be taken at the true "about to open" moment — for a `ComboBox`, that is `AccessKeyInvoked`, which the framework raises *before* it opens the drop-down (confirmed by existing code: `DynamicAccessKeyScope` already listens to `owner.AccessKeyInvoked` to set `openedByAccessKey`, precisely because it fires earlier than `DropDownOpened`). The "after" snapshot and diff happen later, at `DropDownOpened`, against the snapshot captured earlier.

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs` in full. It currently has `FindNewPopup<T>` (Task 1), `WatchPopupOwner` (Task 2, to be replaced by this task), `AddItemBadge` (Task 4), and the static-badge Popup mechanism (Task 5) — only `WatchPopupOwner` changes.

- [ ] **Step 2: Replace `WatchPopupOwner` with `SnapshotOpenPopups` and `InjectIntoNewPopup`**

Delete the entire `WatchPopupOwner` method and its XML doc comment, and replace with:

```csharp
    /// <summary>
    /// Snapshots the popups currently open for this layer's XamlRoot -- call this BEFORE a
    /// popup-owning control's own popup opens, so InjectIntoNewPopup can later diff against it to
    /// find the newly opened one. Split into two methods (this one and InjectIntoNewPopup) instead
    /// of one bracketing call, because a control like ComboBox opens its own drop-down
    /// asynchronously, driven by the framework -- by the time DropDownOpened fires, the popup has
    /// ALREADY opened, so a single "snapshot before, run an action, snapshot after" call has
    /// nothing to bracket: both snapshots would already include the new popup (confirmed live: this
    /// was the root cause of item badges never appearing). The caller must snapshot at the true
    /// "about to open" moment (for a ComboBox, AccessKeyInvoked -- which the framework raises
    /// before it opens the drop-down) and inject at the "opened" moment (DropDownOpened).
    /// </summary>
    public IReadOnlyList<Popup> SnapshotOpenPopups(UIElement owner)
    {
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();
    }

    /// <summary>
    /// Finds the one Popup that opened since <paramref name="before"/> was captured (see
    /// SnapshotOpenPopups) and hands its content Panel to <paramref name="onPopupOpened"/> so item
    /// badges can be added as direct children of it -- sharing the SAME popup as the drop-down, not
    /// a sibling one -- required for them to draw above the drop-down's own content: a ComboBox
    /// drop-down is a light-dismiss Popup, and WinUI keeps light-dismiss popups on a stacking layer
    /// above ordinary Popups regardless of open order, so no sibling Popup can ever draw above it.
    /// Confirmed live via VisualTreeHelper.GetOpenPopupsForXamlRoot.
    ///
    /// Does nothing (onPopupOpened is not called) if no new Popup is found, or if the new Popup's
    /// Child is not a Panel -- both are silent no-ops, not errors.
    /// </summary>
    public void InjectIntoNewPopup(IReadOnlyList<Popup> before, Action<Panel> onPopupOpened)
    {
        var after = VisualTreeHelper.GetOpenPopupsForXamlRoot(_overlay.XamlRoot).ToList();
        var newPopup = FindNewPopup(before, after);

        if (newPopup?.Child is Panel panel)
        {
            onPopupOpened(panel);
        }
    }
```

The `owner` parameter is removed from `SnapshotOpenPopups`'s inner logic (it always uses `_overlay.XamlRoot`, exactly as `WatchPopupOwner` did) but is kept as a parameter for symmetry/future use — if a reviewer flags it as genuinely unused and misleading, that is a legitimate Minor finding; do not treat this note as pre-judging it, decide on the diff itself.

- [ ] **Step 3: Rewrite `DynamicAccessKeyScope.AttachSequentialKeys`'s body**

Replace the full body of the method with:

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

        // Set only when the owner's own access key opened the drop-down, so a mouse or arrow-key
        // open does not start an access-key display session the user never asked for. The popup
        // snapshot is captured HERE, not in DropDownOpened below -- AccessKeyInvoked is the last
        // point before the drop-down's own Popup opens; DropDownOpened fires after it already has,
        // by which point there is nothing left to diff against (see AccessKeyBadgeLayer's
        // SnapshotOpenPopups/InjectIntoNewPopup doc comments for why this two-point split exists).
        var openedByAccessKey = false;
        IReadOnlyList<Popup>? popupsBeforeOpen = null;
        owner.AccessKeyInvoked += (_, _) =>
        {
            openedByAccessKey = true;
            popupsBeforeOpen = badgeLayer.SnapshotOpenPopups(owner);
        };

        owner.DropDownOpened += (_, _) =>
        {
            var continueChord = openedByAccessKey;
            var before = popupsBeforeOpen;
            openedByAccessKey = false;
            popupsBeforeOpen = null;

            owner.DispatcherQueue.TryEnqueue(() =>
            {
                Panel? dropDownPanel = null;
                if (continueChord && before != null)
                {
                    badgeLayer.InjectIntoNewPopup(before, panel => dropDownPanel = panel);
                }

                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is not ComboBoxItem container)
                    {
                        continue;
                    }

                    container.AccessKey = (i + 1).ToString();

                    if (!wired.Add(container))
                    {
                        continue;
                    }

                    container.AccessKeyInvoked += (_, args) =>
                    {
                        onActivated(container, owner.IndexFromContainer(container));
                        args.Handled = true;
                    };

                    if (dropDownPanel != null)
                    {
                        AccessKeyBadgeLayer.AddItemBadge(dropDownPanel, container);
                    }
                }

                if (continueChord)
                {
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
            });
        };
    }
```

Item `AccessKey` assignment and `AccessKeyInvoked` wiring still happen on every open regardless of `continueChord` (matching the class's original stated purpose: "Assigns a sequential digit AccessKey to each item container... as it is realized"), but a badge is only added when `dropDownPanel` was actually found — which only happens when `continueChord` is true, since only then was a valid `before` snapshot captured. This is deliberate, not an oversight: a mouse-opened drop-down has no active display-mode session for a badge to ever render in anyway.

Update the class's doc comment (above `AttachSequentialKeys`) to replace its current sentence about `WatchPopupOwner` with one describing this two-point split, matching the intent shown in the code comment above.

- [ ] **Step 4: Build and run the existing tests**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: 0 errors.

Run: `dotnet test winui3/WinTabber.UI.Common.Tests`
Expected: all tests pass (18/18 as of Task 5) — this task changes no logic any existing test covers directly, but confirm no regression.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "fix: split WatchPopupOwner into snapshot/inject so ComboBox item badges actually appear"
```

---

### Task 7: Remove debug instrumentation and verify live in the app

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Consumes: nothing new — this task only deletes code.
- Produces: nothing new.

The live debugging session that led to this plan left temporary logging in both files: a public `Diag` method and its call sites, all marked `// TEMP-DIAG`, plus a `ShouldConstrainToRootBounds = false` line on the now-removed per-item sibling-`Popup` path (Task 3 already removes the code path that line lived on; if it survives in the diff after Task 3/4, remove it here too, since item badges no longer use a `Popup` of their own).

- [ ] **Step 1: Remove all `TEMP-DIAG` code**

Search both files for the literal string `TEMP-DIAG` and delete:
- The `Diag` method on `AccessKeyBadgeLayer`.
- Every call to `Diag(...)` or `badgeLayer.Diag(...)`.
- The `System.IO`-based file-logging `using` directives, if they become unused after removing `Diag` (check: `System.IO.File.AppendAllLines` was called with a fully-qualified name in the debugging session, so no `using System.IO;` needs removing — confirm by searching the file for any other `System.IO` usage before assuming this).

- [ ] **Step 2: Normalize line endings**

`AccessKeyBadgeLayer.cs` was saved with CRLF line endings during debugging; the rest of the repository uses LF (confirmed: `git diff --ignore-space-at-eol` on this file showed a much smaller diff than a plain `git diff`, meaning most of the 194-line diff at the time was line-ending noise, not real changes).

Run (from the repo root):
```bash
dos2unix winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs
```
If `dos2unix` is not available, use:
```bash
sed -i 's/\r$//' winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs
```

Verify: `file winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs` reports `ASCII text` with no `CRLF` mention (compare to `DynamicAccessKeyScope.cs`, which was never affected and can serve as the reference for "correct").

- [ ] **Step 3: Build and run the full `WinTabber.UI.Common.Tests` suite**

Run: `dotnet build winui3/WinTabber.UI.Common.Tests` then `dotnet test winui3/WinTabber.UI.Common.Tests`
Expected: 0 build errors, all tests pass (the two from Task 1 plus the pre-existing `AccessKeyBadgeTests`).

- [ ] **Step 4: Launch the real app and verify manually**

This mechanism cannot be exercised by an automated test — it depends on live `Popup` stacking behavior in a running WinUI window, which is exactly what the entire debugging session leading to this plan established (see `docs/superpowers/plans/2026-09-18-hint-overlay-winui3.md`'s own testing section for the precedent: `DynamicAccessKeyScope`'s access-key behavior is desktop-requiring and verified by manual run, not by unit test).

Run (from repo root):
```bash
pwsh -c "Get-Process WinTabberUI -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet build winui3/WinTabberUI/WinTabberUI.csproj -p:Platform=x64"
pwsh -c "Start-Process (Get-ChildItem winui3/WinTabberUI/bin/x64/Debug -Recurse -Filter WinTabberUI.exe | Select-Object -First 1).FullName"
```

Manually check, in the running app:
1. Press Alt, then a `ComboBox`'s own access key. The drop-down opens.
2. Confirm the numbered item badges (`1`, `2`, `3`, ...) are visible **above** the drop-down's own item content, not hidden behind it.
3. Confirm the top-level badges (for `PrevButton`, `PlayPauseButton`, etc.) still show correctly at their normal positions — this task did not touch that path, but confirm no regression.
4. Press a digit key to activate an item. Confirm the correct item is selected and the drop-down closes.
5. Dismiss the chord (Escape or click away). Confirm all badges disappear, including item badges — the drop-down closing removes its own `Popup`, which removes the item badges as its children automatically; confirm this actually happens rather than leaving orphaned visuals.

Report the actual observed result for each of the 5 checks before considering this task complete — do not mark this step done from code reading alone.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "chore: remove temporary popup-diagnostic logging"
```

---

## Known follow-up (not in scope for this plan)

Item badges added via `AddItemBadge` do not go through `AccessKeyBadgeLayer`'s shared `_badges` dictionary, so `AccessKeyBadgeLayer`'s stale-key-reuse guard (the check at the top of `OnAccessKeyDisplayRequested` that recreates a badge when a reused container's `AccessKey` changed) does not apply to them. This is safe today because `DynamicAccessKeyScope`'s `wired` `HashSet` already prevents re-wiring a reused container's events more than once, and a `ComboBoxItem`'s digit key never changes across re-opens of the same drop-down instance (only across drop-down open/close, which recreates the `Popup` and therefore the badges with it). If a future change makes item keys change while a container is reused without a full drop-down close, this would need its own guard — flag this to whoever picks up that change rather than assuming it is already handled.
