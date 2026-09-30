# Access-Key Overlay Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Draw access-key badges (including `ComboBox` item badges) on a separate, click-through,
always-owned-above OS window, so they draw above everything in their owning window — including an
open drop-down — without depending on WinUI's own popup-stacking rules.

**Architecture:** A new `AccessKeyOverlayWindow` (chromeless `WindowEx`, one per hinted app window,
sized to that window's current monitor, click-through and kept above its owner via Win32 extended
window styles) replaces `AccessKeyBadgeLayer`'s current `Canvas`+`Popup`-per-badge mechanism.
`DynamicAccessKeyScope` drops its popup-diffing entirely and goes back to calling the same
`Watch(element)` every static element uses.

**Tech Stack:** WinUI 3 (Windows App SDK 1.8), C#, TUnit, CsWin32 (new to `winui3/`, first use there).

**Spec:** `docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md`

## Global Constraints

- The overlay covers only the owning window's current monitor — never the whole virtual desktop,
  never the owning window's own (smaller) bounds.
- Click-through (`WS_EX_TRANSPARENT` + `WS_EX_LAYERED`) and never-activates (`WS_EX_NOACTIVATE`) —
  the overlay must never intercept a click or steal focus.
- Kept above its owner via the Win32 *owned-window* relationship (`GWLP_HWNDPARENT`), not
  WinUIEx's `IsAlwaysOnTop` (which floats above unrelated apps too — out of scope here).
- CsWin32-backed Win32 that affects this app's own window rendering lives beside the WinUI code
  that owns it (`winui3/WinTabber.UI.Common/AccessKeys/`), not in `WinTabber.Interop` — per this
  repo's own architecture rule (see `CLAUDE.md`, "Windows Interop").
- `AccessKeyBadgeLayer.Watch(UIElement element)`'s public signature does not change.
- `DynamicAccessKeyScope.AttachSequentialKeys`'s public signature does not change.
- The three existing `AttachSequentialKeys`/`Watch`/`RegisterAccessKeyBadges` call sites in
  `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs` and
  `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs` must still compile — only the
  `AccessKeyBadgeLayer` construction call itself changes (its constructor's parameter type changes
  from `Canvas` to the owning `WindowEx`).
- CsWin32's exact generated signature for `SetWindowLongPtr`/`GetWindowLongPtr` may differ slightly
  from what a task shows (parameter/return types across CsWin32 versions have varied historically).
  Where a task's shown interop code does not compile against the actual generated signature, fix the
  call to match the generated one — this is a verification step against a generated API, not a
  design change, and does not require asking before proceeding (precedented in this repo: see
  `docs/superpowers/specs/2026-09-17-hint-overlay-winui3-design.md`'s own "verified against the
  actual installed package" section for the same class of adjustment).

---

### Task 1: `AccessKeyOverlayWindow` — chromeless, monitor-sized, click-through, owned window

**Files:**
- Create: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs`
- Create: `winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt`
- Modify: `winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj` (add CsWin32 package reference)

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `public sealed class AccessKeyOverlayWindow : WinUIEx.WindowEx` with
  `public AccessKeyOverlayWindow(Microsoft.UI.Xaml.Window owner)`, `public Canvas Content { get; }`
  (the `Canvas` badges are added to — distinct from `Window.Content`, which IS that same `Canvas`;
  exposed as a typed property so callers never need to downcast `Window.Content`),
  `public void ShowOverlay()`, `public void HideOverlay()`. Task 3 consumes all four.

This window has no XAML file — its content is entirely runtime-driven (badges added/removed as the
chord progresses), matching `AccessKeyBadge`'s own existing precedent of being "built in code, not
XAML, since instances are created and destroyed dynamically." If `WindowEx` cannot be constructed
without a paired `.xaml`/`InitializeComponent()` (no such code-only `WindowEx` subclass exists yet
elsewhere in this repo to confirm either way), fall back to a minimal `.xaml` file with an empty
`<winuiex:WindowEx>` root and set everything else in code-behind exactly as shown below — this is a
build-time discovery, not a design change; note which path was needed in your report.

- [ ] **Step 1: Add the CsWin32 package reference**

In `winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`, inside the existing `<ItemGroup>` that
has `<PackageReference Include="Microsoft.WindowsAppSDK" />`, add:

```xml
        <PackageReference Include="Microsoft.Windows.CsWin32" />
```

The version is already pinned centrally (this package is already referenced by
`WinTabberUI/WinTabberUI.csproj` and the WPF-side `WinTabber.UI.Common.csproj`) — no version
attribute needed, matching how every other `PackageReference` in this file is written.

- [ ] **Step 2: Create the NativeMethods.txt**

Create `winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt`:

```
SetWindowLongPtr
GetWindowLongPtr
```

Per this repo's own rule (`CLAUDE.md`): never list a type with no call site. Step 4 below is that
call site, in the same commit as this file.

- [ ] **Step 3: Write `AccessKeyOverlayWindow`**

```csharp
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// A chromeless, click-through window covering exactly the monitor its owner currently occupies,
/// used to draw access-key badges above everything in the owner -- including an open ComboBox
/// drop-down, which a Popup-based badge (two prior designs, both abandoned) cannot reliably beat:
/// a drop-down is a light-dismiss Popup that WinUI keeps on a stacking layer above every ordinary
/// Popup regardless of open order, and injecting into the drop-down's own internal panel renders
/// inconsistently live (confirmed: correct in logic every time, wrong on screen some of the time --
/// see docs/superpowers/plans/2026-09-27-access-key-popup-zorder-fix.md's ledger). A separate HWND's
/// z-order is controlled by the window manager, not WinUI's popup layering, so it draws above the
/// owner unconditionally once positioned correctly. See
/// docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md for the full design.
///
/// Created once per owning window, at that window's construction; shown and hidden thereafter, never
/// recreated per chord.
/// </summary>
public sealed class AccessKeyOverlayWindow : WinUIEx.WindowEx
{
    private readonly Microsoft.UI.Xaml.Window _owner;
    private readonly Canvas _canvas = new();

    public AccessKeyOverlayWindow(Microsoft.UI.Xaml.Window owner)
    {
        _owner = owner;

        IsTitleBarVisible = false;
        IsShownInSwitchers = false;
        IsResizable = false;
        IsMinimizable = false;
        IsMaximizable = false;

        Content = _canvas;

        var hwnd = WindowNative.GetWindowHandle(this);
        var ownerHwnd = WindowNative.GetWindowHandle(_owner);
        AccessKeyOverlayInterop.MakeClickThroughAndOwned(hwnd, ownerHwnd);
    }

    public Canvas Content => _canvas;

    /// <summary>Resizes and repositions this window to exactly cover the owner's current monitor,
    /// then shows it. Recomputed on every show rather than tracked continuously -- badges are only
    /// ever positioned while a chord is actively displaying, which starts from a hidden overlay every
    /// time, so a stale monitor bound while hidden is never observable.</summary>
    public void ShowOverlay()
    {
        // Per the design spec's Error handling section: if the owner's monitor cannot be found (no
        // known repro; DisplayAreaFallback.Nearest already means this only returns null if literally
        // no display area exists on the system), skip showing this request's badge rather than throw
        // out of an event handler -- same silent-degrade precedent as AccessKeyOverlayInterop above.
        var ownerHwnd = WindowNative.GetWindowHandle(_owner);
        var windowId = Win32Interop.GetWindowIdFromWindow(ownerHwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        if (displayArea is null)
        {
            return;
        }

        AppWindow.MoveAndResize(displayArea.OuterBounds);
        AppWindow.Show();
    }

    public void HideOverlay()
    {
        AppWindow.Hide();
    }
}
```

Note: `public Canvas Content { get; }` in the Interfaces section above and the `public Canvas
Content => _canvas;` property both name the same member — the plan's Interfaces line describes it as
a property, and the code shows it as an expression-bodied property; these are the same thing, not two
separate requirements.

`Window.Content` (the base `Microsoft.UI.Xaml.Window.Content` property, of type `UIElement`) is set
to `_canvas` in the constructor via the bare `Content = _canvas;` assignment — this resolves to the
base class's settable `Content` property, not the new `Canvas Content` property being declared here
(C#'s member-hiding rules would normally require `new` on the derived property and produce a warning
without it; if the build produces a hiding warning, add `public new Canvas Content => _canvas;` to
silence it, since this is a deliberate, not accidental, hide).

- [ ] **Step 4: Write the click-through/owned-window interop**

Create the interop as a nested concern of `AccessKeyOverlayWindow.cs` — a second, `internal static`
class in the same file (small enough not to warrant its own file, and it has exactly one caller):

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Sets the two Win32 extended-window-style properties AccessKeyOverlayWindow needs and that WinUI 3
/// exposes no managed API for: click-through (so the overlay never intercepts a click meant for
/// whatever it visually covers) and the owned-window relationship (so the window manager always
/// keeps it above its owner, without floating above unrelated apps the way WinUIEx's IsAlwaysOnTop
/// would). Lives beside AccessKeyOverlayWindow, not in WinTabber.Interop, because it affects only
/// this app's own window -- see this plan's Global Constraints and CLAUDE.md's "Windows Interop"
/// section for why that boundary is drawn where it is.
/// </summary>
internal static class AccessKeyOverlayInterop
{
    public static void MakeClickThroughAndOwned(nint overlayHwnd, nint ownerHwnd)
    {
        // Per the design spec's Error handling section: no logging infrastructure exists anywhere
        // in winui3/ today (confirmed: grepped for ILogger/Debug.WriteLine usage, found none), so
        // "log and continue" here means degrade silently rather than throw out of a window
        // constructor -- matching AccessKeyBadge.TryGetResource's existing precedent for a
        // non-critical, visually-recoverable failure. Badges still show; only the click-through and
        // above-owner z-order guarantees would be missing.
        try
        {
            var hwnd = new HWND(overlayHwnd);

            var currentExStyle = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            var newExStyle = currentExStyle
                | (nint)WINDOW_EX_STYLE.WS_EX_LAYERED
                | (nint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT
                | (nint)WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, newExStyle);

            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, ownerHwnd);
        }
        catch (Exception)
        {
            // Intentionally swallowed -- see the comment above this try block.
        }
    }
}
```

If `PInvoke.GetWindowLongPtr`/`SetWindowLongPtr`'s generated parameter or return type does not match
this exactly (e.g. it returns/accepts a distinct wrapper type instead of `nint`), adjust the call to
match what CsWin32 actually generated — see this plan's Global Constraints.

- [ ] **Step 5: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: 0 errors. No test is specified for this task — a `WindowEx`/HWND cannot be constructed
headless (the same limitation `docs/superpowers/plans/2026-09-27-access-key-popup-zorder-fix.md`'s
Task 1 already hit with `Popup`) — verify by build only; live verification is Task 12.

- [ ] **Step 6: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj
git commit -m "feat: add AccessKeyOverlayWindow, a click-through owned overlay for access-key badges"
```

---

### Task 2: Coordinate-transform helper

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs`
- Modify: `winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeLayerTests.cs` (add a test class
  in this same file, or create a sibling `AccessKeyOverlayWindowTests.cs` — see Step 1)

**Interfaces:**
- Consumes: nothing new.
- Produces: `internal static Point ComputeOverlayLocalPosition(Point elementLocal, PointInt32
  ownerPosition, PointInt32 overlayPosition, double scale)` on `AccessKeyOverlayWindow` — Task 3
  consumes this from `AccessKeyBadgeLayer`.

This is the one piece of this design with real, isolatable logic and no WinUI/Win32 construction
requirement — it takes plain point/scale values and returns a point. Test it directly, the same way
`AccessKeyBadge.ComputeUnderlineLength` already is.

- [ ] **Step 1: Write the failing test**

Create `winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyOverlayWindowTests.cs`:

```csharp
using Windows.Foundation;
using Windows.Graphics;
using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Common.Tests.AccessKeys;

public class AccessKeyOverlayWindowTests
{
    [Test]
    public async Task ComputeOverlayLocalPosition_offsets_by_the_difference_between_owner_and_overlay_origin()
    {
        // Owner window sits at physical (2560, 0) -- a second monitor to the right of the primary,
        // which the overlay (anchored to that same monitor) sits at (2560, 0) too in this case, so
        // the offset is zero and the element's own local position passes through unchanged.
        var elementLocal = new Point(24, 8);
        var ownerPosition = new PointInt32(2560, 0);
        var overlayPosition = new PointInt32(2560, 0);
        var scale = 1.5;

        var result = AccessKeyOverlayWindow.ComputeOverlayLocalPosition(elementLocal, ownerPosition, overlayPosition, scale);

        await Assert.That(result.X).IsEqualTo(24d);
        await Assert.That(result.Y).IsEqualTo(8d);
    }

    [Test]
    public async Task ComputeOverlayLocalPosition_adds_the_scaled_owner_overlay_offset()
    {
        // Owner window is offset 300 physical pixels right and 100 down from the overlay's own
        // origin (e.g. overlay anchored to the monitor's top-left, owner window positioned partway
        // into it). At 1.5x scale, that is a 200,~67 DIP offset added to the element's own local
        // position.
        var elementLocal = new Point(10, 10);
        var ownerPosition = new PointInt32(300, 100);
        var overlayPosition = new PointInt32(0, 0);
        var scale = 1.5;

        var result = AccessKeyOverlayWindow.ComputeOverlayLocalPosition(elementLocal, ownerPosition, overlayPosition, scale);

        await Assert.That(result.X).IsEqualTo(10d + 300d / 1.5d);
        await Assert.That(result.Y).IsEqualTo(10d + 100d / 1.5d);
    }
}
```

If this repo's TUnit setup does not resolve `Assert.That(double).IsEqualTo(double)` directly (floating
point equality assertions sometimes need a tolerance overload), check the assertion library actually
referenced by `WinTabber.UI.Common.Tests` and use its real floating-point-comparison method instead —
another build-time verification, not a design change.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyOverlayWindowTests/*"`
Expected: FAIL — `ComputeOverlayLocalPosition` does not exist yet (compile error).

- [ ] **Step 3: Write the minimal implementation**

Add to `AccessKeyOverlayWindow`:

```csharp
    /// <summary>
    /// Converts elementLocal (an element's position in its own window's coordinates, as
    /// TransformToVisual(null) already gives it) into this overlay's own coordinate space. Both
    /// ownerPosition and overlayPosition are each window's AppWindow.Position -- top-left in
    /// physical screen pixels -- so their difference is already in the same physical-pixel space;
    /// dividing by scale converts that difference into the DIPs elementLocal and the overlay's own
    /// Canvas positions are both expressed in. See
    /// docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md's Coordinate placement
    /// section for the full derivation.
    /// </summary>
    internal static Point ComputeOverlayLocalPosition(
        Point elementLocal,
        PointInt32 ownerPosition,
        PointInt32 overlayPosition,
        double scale
    )
    {
        var offsetX = (ownerPosition.X - overlayPosition.X) / scale;
        var offsetY = (ownerPosition.Y - overlayPosition.Y) / scale;
        return new Point(elementLocal.X + offsetX, elementLocal.Y + offsetY);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/AccessKeyOverlayWindowTests/*"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyOverlayWindowTests.cs
git commit -m "test: add ComputeOverlayLocalPosition to AccessKeyOverlayWindow"
```

---

### Task 3: Rebuild `AccessKeyBadgeLayer` around `AccessKeyOverlayWindow`

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`
- Modify: `winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeLayerTests.cs`

**Interfaces:**
- Consumes: `AccessKeyOverlayWindow(Microsoft.UI.Xaml.Window owner)`, `.Content` (the `Canvas`),
  `.ShowOverlay()`, `.HideOverlay()` (Task 1); `AccessKeyOverlayWindow.ComputeOverlayLocalPosition(...)`
  (Task 2).
- Produces: `public AccessKeyBadgeLayer(Microsoft.UI.Xaml.Window owner)` — Task 5 changes its one
  call site to this new constructor shape. `public void Watch(UIElement element)` unchanged.

This replaces the entire body of `AccessKeyBadgeLayer.cs`. Read the current file first — it still
contains `FindNewPopup<T>`, `SnapshotOpenPopups`, `InjectIntoNewPopup`, `AddItemBadge`, and the
per-badge-`Popup` mechanism in `OnAccessKeyDisplayRequested`/`Dismissed`, all of which this task
deletes outright (per the spec's Migration section — none of them are kept as a fallback).

- [ ] **Step 1: Replace `AccessKeyBadgeLayer.cs` in full**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Replaces WinUI 3's default key-tip badge with a custom AccessKeyBadge for every element
/// registered via Watch, drawn on a separate AccessKeyOverlayWindow rather than inside this layer's
/// own window -- see docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md for why:
/// neither a plain Canvas child nor a sibling Popup can reliably draw above an open ComboBox
/// drop-down. AccessKeyDisplayRequested/Dismissed do NOT bubble -- confirmed against metadata
/// (AccessKeyInvokedEventArgs has a Handled property; these two do not) -- so there is no single
/// attachment point that reaches every hinted descendant; each element needs its own Watch call.
/// Requires AccessKeyManager.AreKeyTipsEnabled = false to have been set (once, app-wide, at startup)
/// so the framework's own default badge does not also draw.
/// </summary>
public sealed class AccessKeyBadgeLayer
{
    private readonly Window _owner;
    private readonly AccessKeyOverlayWindow _overlay;
    private readonly Dictionary<UIElement, AccessKeyBadge> _badges = new();

    public AccessKeyBadgeLayer(Window owner)
    {
        _owner = owner;
        _overlay = new AccessKeyOverlayWindow(owner);
    }

    /// <summary>Registers one element to get a custom badge instead of the (disabled) default one.</summary>
    public void Watch(UIElement element)
    {
        element.AccessKeyDisplayRequested += OnAccessKeyDisplayRequested;
        element.AccessKeyDisplayDismissed += OnAccessKeyDisplayDismissed;
    }

    private void OnAccessKeyDisplayRequested(UIElement sender, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs args)
    {
        var underlineLength = AccessKeyBadge.ComputeUnderlineLength(sender.AccessKey, args.PressedKeys);

        if (_badges.TryGetValue(sender, out var existingBadge) && existingBadge.Text != sender.AccessKey)
        {
            // sender's AccessKey changed since this badge was created (DynamicAccessKeyScope
            // reassigns a reused ComboBoxItem's key on every DropDownOpened) -- the cached badge's
            // text is now stale and must not be reused with an underline length computed for a
            // different string.
            _overlay.Content.Children.Remove(existingBadge.Visual);
            _badges.Remove(sender);
        }

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
            _overlay.Content.Children.Add(badge.Visual);
        }

        if (_badges.Count == 1)
        {
            // First badge of this chord -- the overlay is currently hidden (it hides itself once
            // empty, in OnAccessKeyDisplayDismissed below). Shown before positioning, not after: the
            // position computed below needs the overlay's own AppWindow.Position, which
            // ShowOverlay's MoveAndResize call is what sets.
            _overlay.ShowOverlay();
        }

        var elementLocal = sender.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var scale = _owner.Content.XamlRoot.RasterizationScale;
        var overlayLocal = AccessKeyOverlayWindow.ComputeOverlayLocalPosition(
            elementLocal,
            _owner.AppWindow.Position,
            _overlay.AppWindow.Position,
            scale
        );

        // Matches HintPosition.TopLeft's exact offset (bounds.Left - 4, bounds.Top - 4), ported from
        // WPF's HintPosition.cs.
        Canvas.SetLeft(badge.Visual, overlayLocal.X - 4);
        Canvas.SetTop(badge.Visual, overlayLocal.Y - 4);

        badge.UpdatePrefix(underlineLength);
    }

    private void OnAccessKeyDisplayDismissed(UIElement sender, Microsoft.UI.Xaml.Input.AccessKeyDisplayDismissedEventArgs args)
    {
        // A dismiss for an element with no active badge (e.g. dismissed twice) is a no-op, not an
        // error -- Dictionary.Remove's bool return makes that the natural shape here.
        if (_badges.Remove(sender, out var badge))
        {
            _overlay.Content.Children.Remove(badge.Visual);
        }

        if (_badges.Count == 0)
        {
            _overlay.HideOverlay();
        }
    }
}
```

Note: `Microsoft.UI.Xaml.Window` does not itself expose `AppWindow` directly on every SDK version in
every build configuration -- if `_owner.AppWindow` does not resolve, use
`WinRT.Interop.WindowNative.GetWindowHandle(_owner)` plus
`Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd))`
instead, matching the pattern already established in `winui3/WinTabberUI/Views/*.xaml.cs` (every one
of which already uses `this.AppWindow` inside a `WindowEx`-derived partial class successfully) — this
is the same class of build-time verification named in this plan's Global Constraints.

- [ ] **Step 2: Rewrite `AccessKeyBadgeLayerTests.cs`**

The existing tests target `FindNewPopup`, which this task deletes. Replace the file's contents
entirely:

```csharp
namespace WinTabber.UI.Common.Tests.AccessKeys;

// AccessKeyBadgeLayer itself cannot be constructed headless (it now owns an AccessKeyOverlayWindow,
// a real WinUI window) -- see docs/superpowers/plans/2026-09-27-access-key-popup-zorder-fix.md's
// Task 1 for the same limitation previously hit with Popup construction. Its one piece of pure logic
// (position math) now lives on AccessKeyOverlayWindow and is tested there --
// see AccessKeyOverlayWindowTests.cs. This file is intentionally empty of test classes; kept only so
// a future addition to AccessKeyBadgeLayer that IS pure logic has an obvious home.
```

- [ ] **Step 3: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: 0 errors. Note this project alone will not launch anything runnable — a full end-to-end
build (including `WinTabberUI`) happens once Task 5 updates the one real call site.

- [ ] **Step 4: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests`
Expected: PASS — the `AccessKeyBadgeTests` (unrelated, still cover `ComputeUnderlineLength`) and the
new `AccessKeyOverlayWindowTests` (Task 2) both still pass; no test targets the now-empty
`AccessKeyBadgeLayerTests.cs`, which is expected.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs winui3/WinTabber.UI.Common.Tests/AccessKeys/AccessKeyBadgeLayerTests.cs
git commit -m "feat: rebuild AccessKeyBadgeLayer around AccessKeyOverlayWindow"
```

---

### Task 4: Simplify `DynamicAccessKeyScope` back down

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Consumes: `AccessKeyBadgeLayer.Watch(UIElement element)` (unchanged shape, now backed by Task 3's
  rebuilt implementation).
- Produces: no change to `AttachSequentialKeys`'s public signature.

The popup-diffing mechanism this class built up over two prior (now-abandoned) designs
(`SnapshotOpenPopups`/`InjectIntoNewPopup`/`AddItemBadge`, and the `openedByAccessKey` chord-timing
capture that fed it) is no longer needed for badge placement — `Watch(container)` now works for a
`ComboBoxItem` exactly as it does for any static element, since badges live on a window that draws
above everything regardless of when they were opened. The Alt-toggle continuity behavior itself
(pressing the `ComboBox`'s own access key and having its items immediately show their own badges,
without a second Alt press) still needs the `EnterDisplayMode` call — that part of the design was
correct and stays; only the popup-snapshot machinery goes.

- [ ] **Step 1: Replace `DynamicAccessKeyScope.cs` in full**

```csharp
using Microsoft.UI.Xaml.Controls;
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
    /// access-key badge/chord session continuous across the transition from the ComboBox's own key
    /// into its items' keys -- without both, the user must press Alt a second time after the
    /// drop-down opens. The HashSet dedup is required because DropDownOpened fires on every open and
    /// containers can be reused; without it, AccessKeyInvoked handlers would stack across repeated
    /// opens. Item badges use the SAME badgeLayer.Watch(container) call any static element uses --
    /// no special popup-aware path needed, since badges now draw on a separate overlay window that
    /// sits above the drop-down regardless (see
    /// docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md).
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

                    badgeLayer.Watch(container);
                    container.AccessKeyInvoked += (_, args) =>
                    {
                        onActivated(container, owner.IndexFromContainer(container));
                        args.Handled = true;
                    };
                }

                if (continueChord)
                {
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
            });
        };
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "fix: drop popup-diffing from DynamicAccessKeyScope, item badges use the same Watch path"
```

---

### Task 5: Wire up the one real call site

**Files:**
- Modify: `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs`
- Modify: `winui3/WinTabberUI/Views/MediaControlsWindow.xaml`

**Interfaces:**
- Consumes: `AccessKeyBadgeLayer(Microsoft.UI.Xaml.Window owner)` (Task 3).
- Produces: nothing new — this is the plan's only consumer-facing change.

**Files NOT touched by this task, confirmed unaffected:** `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs`'s
`RegisterAccessKeyBadges(AccessKeyBadgeLayer layer)` only calls `layer.Watch(...)` — no `Canvas`
reference, no constructor call — so it needs no change.

- [ ] **Step 1: Update the construction call**

In `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs`, find:

```csharp
        var accessKeyBadgeLayer = new AccessKeyBadgeLayer(AccessKeyOverlay);
```

Replace with:

```csharp
        var accessKeyBadgeLayer = new AccessKeyBadgeLayer(this);
```

Every other line in that block (`accessKeyBadgeLayer.Watch(SessionSelector)`,
`DynamicAccessKeyScope.AttachSequentialKeys(SessionSelector, accessKeyBadgeLayer, ...)`, etc.) is
unchanged — `AccessKeyBadgeLayer`'s public shape besides its constructor did not change.

- [ ] **Step 2: Remove the now-unused overlay `Canvas` from the XAML**

In `winui3/WinTabberUI/Views/MediaControlsWindow.xaml:343`, remove:

```xml
        <Canvas x:Name="AccessKeyOverlay" Grid.RowSpan="2" IsHitTestVisible="False" />
```

and its preceding comment (`<!-- Topmost: AccessKeyBadgeLayer (constructed in code-behind) draws
custom access-key ... -->`, around line 339) — both describe a mechanism this plan replaces. Confirm
no other code references `AccessKeyOverlay` by name before removing it (the construction call in
Step 1 was its only reference).

- [ ] **Step 3: Build the full app**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs winui3/WinTabberUI/Views/MediaControlsWindow.xaml
git commit -m "fix: construct AccessKeyBadgeLayer with its owning window, not a Canvas"
```

---

### Task 6: Fix overlay transparency and focus-stealing on show

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs`
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt`

**Interfaces:**
- Consumes: nothing new.
- Produces: no change to `AccessKeyOverlayWindow`'s public shape (`ShowOverlay()`/`HideOverlay()`/
  `Content` unchanged) — only their internal implementation changes.

**Why this task exists:** live testing after Task 5 found two real bugs Task 1 did not anticipate:

1. **The overlay is not visually transparent** — it shows as an opaque window instead of only its
   badge visuals being visible. `AccessKeyOverlayWindow` never set `SystemBackdrop`, and a WinUI 3
   `Window` with no backdrop set does not default to true per-pixel transparency on its own.
2. **The overlay steals keyboard focus when shown, blocking access-key activation entirely** —
   despite `WS_EX_NOACTIVATE` being set (Task 1). `WS_EX_NOACTIVATE` only suppresses *implicit*
   OS-driven activation (e.g. a click); `AppWindow.Show()` (used by `ShowOverlay()`) issues an
   explicit activating show, which overrides it. The correct fix is not a different extended style
   but a different show call: `ShowWindow(hwnd, SW_SHOWNOACTIVATE)`, the standard Win32 API for
   showing a window without activating it, called directly instead of `AppWindow.Show()`.

**Transparency fix:** set `SystemBackdrop` to a `WinUIEx.TransparentTintBackdrop` with `TintOpacity =
0` (confirmed real, settable properties on the installed `WinUIEx 2.9.3` package via direct
inspection of `WinUIEx.dll` before this task was written — `TintOpacity`/`DarkTintOpacity` are both
public settable properties on that class). This gives a fully transparent backdrop while the
overlay's own `Canvas`/badge visuals still draw normally on top, since they are ordinary opaque XAML
content, not part of the backdrop.

**Focus fix:** add `ShowWindow` to this project's `NativeMethods.txt` and call
`PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE)` from `ShowOverlay()` instead of
`AppWindow.Show()`. This is CsWin32-backed, consistent with this project's established pattern (per
this plan's Global Constraints) for Win32 affecting the app's own window.

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs` in full — only the
constructor (add the backdrop) and `ShowOverlay()` (replace the show call) change.

- [ ] **Step 2: Add the transparent backdrop in the constructor**

Add, inside the constructor, after the existing `IsMaximizable = false;` line and before
`Content = _canvas;`:

```csharp
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop { TintOpacity = 0 };
```

- [ ] **Step 3: Add `ShowWindow` to `NativeMethods.txt`**

Add one line to `winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt` (three existing lines stay
as-is; this becomes a fourth):

```
ShowWindow
```

- [ ] **Step 4: Replace `ShowOverlay()`'s show call**

Replace:

```csharp
        AppWindow.MoveAndResize(displayArea.OuterBounds);
        AppWindow.Show();
```

with:

```csharp
        AppWindow.MoveAndResize(displayArea.OuterBounds);

        var hwnd = WindowNative.GetWindowHandle(this);
        PInvoke.ShowWindow(new HWND(hwnd), SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
```

Add `using Windows.Win32;` and `using Windows.Win32.Foundation;` to the file's top if not already
present (the `AccessKeyOverlayInterop` class in this same file already uses both — check before
adding a duplicate `using`).

- [ ] **Step 5: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors. Remember the `-p:Platform=x64` flag — a plain `dotnet build` on this project
silently drops x64-only CsWin32 members (diagnosed earlier in this plan's history).

- [ ] **Step 6: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass — this task changes no logic any existing test covers.

- [ ] **Step 7: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt
git commit -m "fix: make the access-key overlay window actually transparent and non-activating"
```

---

### Task 7: Fix z-order — overlay must show above its owner, not behind it

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs`
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt`

**Interfaces:**
- Consumes: nothing new.
- Produces: no change to `AccessKeyOverlayWindow`'s public shape — only `ShowOverlay()`'s internal
  implementation changes.

**Why this task exists:** live testing after Task 6 found the overlay now shows transparently and
without stealing focus, but *behind* the owning `MediaControlsWindow` instead of above it. Task 6
replaced `AppWindow.Show()` (which brings a window to the top of its local z-order as part of
showing/activating it) with the non-activating `PInvoke.ShowWindow(hwnd, SW_SHOWNOACTIVATE)` — but
`ShowWindow` only changes a window's visibility, not its z-order position. The Win32 owned-window
relationship (`GWLP_HWNDPARENT`, set in Task 1) keeps an owned window above its owner only as an
initial/typical convention, not as a strict invariant the window manager continuously re-enforces —
it does not, by itself, guarantee the overlay is brought to the top of the owner's local stack every
time it is shown, especially given the owner (not the overlay) is the one that keeps receiving actual
activation throughout the app's use.

**The fix:** after `ShowWindow`, explicitly call `SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE
| SWP_NOSIZE | SWP_NOACTIVATE)` — the standard, minimal Win32 technique for bringing a window to the
top of its local z-order without moving it, resizing it, or activating it. `HWND_TOP` (not
`HWND_TOPMOST`) is deliberate: it places the overlay at the top of the *normal* z-order band, which
is enough given the owned-window relationship already keeps it grouped with its owner — matching this
plan's Global Constraint against using a system-wide-topmost mechanism.

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs` in full — only `ShowOverlay()`
changes.

- [ ] **Step 2: Add `SetWindowPos` to `NativeMethods.txt`**

Add one line to `winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt` (four existing lines stay
as-is; this becomes a fifth):

```
SetWindowPos
```

- [ ] **Step 3: Add the `SetWindowPos` call after `ShowWindow`**

Replace:

```csharp
        var hwnd = WindowNative.GetWindowHandle(this);
        PInvoke.ShowWindow(new HWND(hwnd), SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
```

with:

```csharp
        var hwnd = new HWND(WindowNative.GetWindowHandle(this));
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);

        // ShowWindow only changes visibility, not z-order -- the owned-window relationship
        // (GWLP_HWNDPARENT, set in the constructor) keeps this window grouped with its owner but
        // does not guarantee it is brought to the top of that group every time it is shown,
        // especially since the owner (not this window) is what keeps receiving real activation.
        // HWND_TOP (not HWND_TOPMOST) brings it to the top of the normal z-order band only --
        // enough given the owned-window grouping, and consistent with this plan's decision against
        // a system-wide-topmost mechanism.
        PInvoke.SetWindowPos(
            hwnd,
            HWND.HWND_TOP,
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
        );
```

If `HWND.HWND_TOP` is not how CsWin32 exposes that well-known handle constant (CsWin32 has
represented Win32's special `HWND_*` constants differently across versions — sometimes as
`HWND` static fields, sometimes as a separate enum), adjust to whatever the actual generated API
provides — this is a verification step against a generated API, per this plan's Global Constraints,
not a design change.

- [ ] **Step 4: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 5: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass.

- [ ] **Step 6: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt
git commit -m "fix: bring the access-key overlay to the top of its z-order band on every show"
```

---

### Task 8: Fix z-order against a topmost owner, and strip the residual window border

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no change to `AccessKeyOverlayWindow`'s public shape — only the constructor and
  `ShowOverlay()`'s internal implementation change. No `NativeMethods.txt` change needed (no new
  CsWin32 function — `HWND_TOPMOST` is a constant, not a function, and `SetWindowLongPtr`/
  `WINDOW_EX_STYLE` for the border fix are already listed).

**Why this task exists:** live testing after Task 7 found two more issues, both root-caused with a
diagnostic pass (temporary logging, added and fully reverted before this task was written — see this
plan's ledger for the captured evidence) rather than guessed:

1. **Z-order still wrong.** `MediaControlsWindow` (the owner) sets `IsAlwaysOnTop="True"` in its own
   XAML — confirmed via `grep` — which is `WS_EX_TOPMOST`. Confirmed live: the owner's
   `GWL_EXSTYLE` has bit `0x8` (`WS_EX_TOPMOST`) set. `SetWindowPos(..., HWND_TOP, ...)` (Task 7)
   only reorders a window within the *non-topmost* z-order band, which sits entirely below every
   topmost window — so the overlay could never rise above a topmost owner no matter how it was
   reordered within that lower band. The owned-window relationship (`GWLP_HWNDPARENT`) does not
   retroactively make an owned window topmost when the owner already is; confirmed live via
   `GetWindow(overlay, GW_OWNER)` correctly returning the owner (the relationship itself is fine) —
   the bug is specifically about which z-order band the fix targeted.
2. **A visible 1px border traces the outer edge of the screen.** Confirmed live: the overlay's
   `GWL_STYLE` has `WS_BORDER` (`0x800000`) and `WS_DLGFRAME` (`0x400000`) set (together, `WS_CAPTION`,
   `0xC00000`) despite `IsTitleBarVisible = false` in the constructor — that WinUIEx property does
   not clear these underlying `GWL_STYLE` bits. At full-monitor size, this ordinarily-subtle window
   border becomes a visible line around the entire screen.

**The z-order fix:** use `HWND_TOPMOST` (`(HWND)(-1)`) instead of `HWND_TOP` in the `SetWindowPos`
call. This reverses this plan's own earlier Global Constraint choosing `HWND_TOP`/owned-window
grouping specifically to avoid a system-wide-topmost overlay — but that constraint's stated reason
("floats above unrelated apps too") does not actually apply here: the overlay is hidden except while
a chord is actively displaying, so there is no meaningful window in which it would float above
anything the user is not already interacting with. This ruling is recorded in the ledger, not silently
overridden.

**The border fix:** clear `WS_CAPTION` (which covers both `WS_BORDER` and `WS_DLGFRAME`) from
`GWL_STYLE` directly, the same way `GWL_EXSTYLE` is already modified in this file — no new CsWin32
function needed, `SetWindowLongPtr`/`WINDOW_STYLE` are effectively already available (the file already
uses `WINDOW_LONG_PTR_INDEX`; `GWL_STYLE` is one of its existing members, and `WINDOW_STYLE` needs
adding to `NativeMethods.txt` for the `WS_CAPTION` constant to resolve).

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs` in full — the constructor
gains one new interop call (border strip), and `ShowOverlay()`'s `SetWindowPos` call changes its
second argument only.

- [ ] **Step 2: Add `WINDOW_STYLE` to `NativeMethods.txt`**

Add one line to `winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt` (five existing lines stay
as-is; this becomes a sixth):

```
WINDOW_STYLE
```

- [ ] **Step 3: Strip the border style in `AccessKeyOverlayInterop.MakeClickThroughAndOwned`**

This method already sets `GWL_EXSTYLE`; add the `GWL_STYLE` change in the same method, inside the
same `try` block, right after the existing `GWL_EXSTYLE`/`GWLP_HWNDPARENT` calls:

```csharp
            var currentStyle = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            var newStyle = currentStyle & ~(nint)WINDOW_STYLE.WS_CAPTION;
            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, newStyle);
```

Update the method's XML doc comment to mention this third responsibility (it currently says "the
two Win32 extended-window-style properties" — this is now a third, non-extended-style property).

- [ ] **Step 4: Fix the z-order call in `ShowOverlay()`**

Replace:

```csharp
        PInvoke.SetWindowPos(
            hwnd,
            default(HWND),
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
        );
```

with:

```csharp
        // HWND_TOPMOST, not HWND_TOP: the owner (MediaControlsWindow) sets IsAlwaysOnTop="True"
        // (WS_EX_TOPMOST), confirmed live via its GWL_EXSTYLE. HWND_TOP only reorders within the
        // non-topmost z-order band, which sits entirely below every topmost window -- against a
        // topmost owner, no reordering within that lower band can ever place this window above it.
        // This reverses this plan's original choice of HWND_TOP specifically to avoid a
        // system-wide-topmost overlay, but that concern doesn't apply in practice: the overlay is
        // hidden except while a chord is actively displaying, so there's no window of time in which
        // it would float above anything the user isn't already interacting with. CsWin32 does not
        // generate a named HWND_TOPMOST constant either (same reason as HWND_TOP: a header macro,
        // not a metadata member) -- it is ((HWND)-1), passed here as (HWND)(-1).
        PInvoke.SetWindowPos(
            hwnd,
            (HWND)(-1),
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
        );
```

If `(HWND)(-1)` does not compile against the actual generated `HWND` struct (e.g. it has no explicit
conversion from `int`), construct it however the generated type actually allows (its underlying field
is a pointer-sized value; `new HWND((nint)(-1))` or equivalent) — a verification step against a
generated API, not a design change, per this plan's Global Constraints.

- [ ] **Step 5: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 6: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass.

- [ ] **Step 7: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs winui3/WinTabber.UI.Common/AccessKeys/NativeMethods.txt
git commit -m "fix: use HWND_TOPMOST against a topmost owner, and strip the overlay's window border"
```

---

### Task 9: Fix the persistent border and the overlay not hiding with its owner

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs`
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no change to either class's public shape.

**Why this task exists:** live testing plus a reverted diagnostic pass (evidence recorded in this
plan's ledger) found two more bugs:

1. **The border is still visible.** Diagnostic evidence: `ShowOverlay` logged `WS_CAPTION_bits=0x800000`
   (`WS_BORDER` still set) at show time, despite Task 8's constructor-time `GWL_STYLE` clear. The style
   bit gets reapplied sometime between construction and the window's first real show — most likely by
   the `WindowEx`/`AppWindow` presenter realizing its own default frame the first time the window
   becomes visible, which happens after the constructor runs. A one-time clear in the constructor is
   not enough; the clear must also happen (or be re-applied) at show time, and the frame needs to be
   told to redraw via `SWP_FRAMECHANGED`.
2. **The overlay does not hide when its owner (`MediaControlsWindow`) is hidden**, only when the chord
   itself is dismissed. This is standard, documented Win32 behavior, not a logic bug: an owned window
   is automatically hidden by the window manager when its owner is *minimized*, but not when the owner
   is hidden via `SW_HIDE` (which is how this app's own show/hide toggle likely works, going through
   `AppWindow.Hide()`/similar rather than minimizing) — nothing currently tells the badge layer that
   the owner went away.

**The border fix:** re-apply the `GWL_STYLE` clear inside `ShowOverlay()` (not only in the
constructor), and add `SWP_FRAMECHANGED` to the existing `SetWindowPos` call's flags so DWM actually
redraws the non-client area to reflect the cleared style — a style change alone does not force a
repaint.

**The hide fix:** `AccessKeyBadgeLayer` subscribes to the owner's `AppWindow.Changed` event; when
`args.DidVisibilityChange` fires and `_owner.AppWindow.IsVisible` is now `false`, clear every badge
(remove each visual from `_overlay.Content.Children`, clear the `_badges` dictionary) and hide the
overlay — mirroring what `OnAccessKeyDisplayDismissed` already does when a chord ends normally, so
there is no third copy of "how to clear all badges" logic. Also change `HideOverlay()` to use
`PInvoke.ShowWindow(hwnd, SW_HIDE)` instead of `AppWindow.Hide()`, for consistency with `ShowOverlay()`
already using the raw `ShowWindow` API (Task 6's reviewer flagged this asymmetry as worth confirming
live; it is now confirmed worth fixing).

- [ ] **Step 1: Read both files first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs` and
`winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs` in full.

- [ ] **Step 2: Split the border-clearing logic out of `MakeClickThroughAndOwned` so `ShowOverlay()` can call it too**

In `AccessKeyOverlayInterop`, split the existing single `MakeClickThroughAndOwned` method into two —
keep the click-through/owned-window logic as-is, and extract the `GWL_STYLE` clear into its own
method so `ShowOverlay()` can call it independently:

```csharp
    public static void ClearBorder(HWND hwnd)
    {
        try
        {
            var currentStyle = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            var newStyle = currentStyle & ~(nint)WINDOW_STYLE.WS_CAPTION;
            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, newStyle);
        }
        catch (Exception)
        {
            // Intentionally swallowed -- same silent-degrade rationale as MakeClickThroughAndOwned.
        }
    }
```

Remove the `GWL_STYLE` block from `MakeClickThroughAndOwned` (it now only sets `GWL_EXSTYLE` and
`GWLP_HWNDPARENT` again, as it did before Task 8) and call `ClearBorder` separately from the
constructor:

```csharp
        var hwnd = WindowNative.GetWindowHandle(this);
        var ownerHwnd = WindowNative.GetWindowHandle(_owner);
        AccessKeyOverlayInterop.MakeClickThroughAndOwned(hwnd, ownerHwnd);
        AccessKeyOverlayInterop.ClearBorder(new HWND(hwnd));
```

In `ShowOverlay()`, call `ClearBorder` again right before the existing `SetWindowPos` call, and add
`SWP_FRAMECHANGED` to that call's flags:

```csharp
        AccessKeyOverlayInterop.ClearBorder(hwnd);

        PInvoke.SetWindowPos(
            hwnd,
            (HWND)(-1),
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED
        );
```

Call `AccessKeyOverlayInterop.ClearBorder(hwnd)` directly from both the constructor and `ShowOverlay()`
— both already have an `HWND` in scope, so no additional wrapper method is needed.

- [ ] **Step 3: Change `HideOverlay()` to use `ShowWindow(SW_HIDE)`**

Replace:

```csharp
    public void HideOverlay()
    {
        AppWindow.Hide();
    }
```

with:

```csharp
    public void HideOverlay()
    {
        var hwnd = new HWND(WindowNative.GetWindowHandle(this));
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    }
```

- [ ] **Step 4: Subscribe to the owner's visibility in `AccessKeyBadgeLayer`'s constructor**

In `AccessKeyBadgeLayer.cs`, add to the constructor:

```csharp
    public AccessKeyBadgeLayer(Window owner)
    {
        _owner = owner;
        _overlay = new AccessKeyOverlayWindow(owner);

        // An owned window is hidden by the window manager when its owner is minimized, but NOT when
        // the owner is hidden via SW_HIDE (how this app's own show/hide toggle works) -- nothing else
        // tells this layer the owner went away, so badges could otherwise linger visible over nothing.
        _owner.AppWindow.Changed += OnOwnerAppWindowChanged;
    }

    private void OnOwnerAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange || sender.IsVisible)
        {
            return;
        }

        foreach (var badge in _badges.Values)
        {
            _overlay.Content.Children.Remove(badge.Visual);
        }
        _badges.Clear();
        _overlay.HideOverlay();
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 6: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass.

- [ ] **Step 7: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyOverlayWindow.cs winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs
git commit -m "fix: re-apply the border clear at show time, and hide the overlay when its owner hides"
```

---

### Task 10: Fix ComboBox item badges never displaying on first open

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no change to `AttachSequentialKeys`'s public signature.

**Why this task exists:** live testing plus a reverted diagnostic pass (evidence in this plan's
ledger) found that `ComboBoxItem` badges never display, in either of two consecutive attempts. The
diagnostic log showed the drop-down opening with `continueChord=True` and
`AccessKeyManager.IsDisplayModeEnabled` already `True` at that point (display mode was entered by the
user's own Alt press before the `ComboBox`'s key was pressed) — and zero `AccessKeyDisplayRequested`
events ever fired for any `ComboBoxItem`, in either attempt. `AccessKeyManager.EnterDisplayMode` is a
no-op when display mode is already enabled, so the framework never re-scans the newly-added item
access keys to ask them to display. **This is the original bug reported at the very start of this
session**, not a defect introduced by the overlay-window design — the overlay changes only affect how
a badge is drawn, not whether the framework ever asks for one to be drawn at all.

**User decision:** exit and re-enter display mode when the drop-down opens, forcing the framework to
re-scan every element in the current scope (root elements and the newly-realized items alike) and ask
each to display. Accepted trade-off: the root-level badges will blink off and back on for one frame
when a `ComboBox`'s drop-down opens, since they are also in scope and get the same treatment — this
uses the framework's own re-scan mechanism rather than adding a second, parallel path for item badges.

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs` in full — only the final
`if (continueChord)` block changes.

- [ ] **Step 2: Exit and re-enter display mode instead of only entering it**

Replace:

```csharp
                if (continueChord)
                {
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
```

with:

```csharp
                if (continueChord)
                {
                    // EnterDisplayMode alone is a no-op here -- display mode is already on (the
                    // user's own Alt press turned it on before this ComboBox's key was pressed), so
                    // the framework never re-scans and never asks the newly-assigned item keys to
                    // display (confirmed live: zero AccessKeyDisplayRequested events ever fired for
                    // any ComboBoxItem without this). Exiting first forces a re-scan on entry, which
                    // does ask every element in scope -- root elements included, which is why they
                    // blink off and back on for one frame; accepted trade-off, see the plan/spec.
                    AccessKeyManager.ExitDisplayMode();
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
```

- [ ] **Step 3: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass — this task changes no logic any existing test covers.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "fix: exit and re-enter display mode so ComboBox item keys actually get asked to display"
```

---

### Task 11: Replace the display-mode workaround with the framework's own scope mechanism

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no change to `AttachSequentialKeys`'s public signature.

**Why this task exists:** Task 10's exit/re-enter fix made item badges display, but live testing found
a side effect: root-level badges (`Prev`/`Play-Pause`/etc.) stayed visible alongside the item badges
instead of hiding — `EnterDisplayMode(owner.XamlRoot)` re-enters at the whole window's root scope, so
it re-asks every top-level element to display too, not just the `ComboBox`'s own items.

A follow-up spike (temporary logging and a temporary `AccessKeyScopeOwner` assignment, run once and
fully reverted before this task was written — evidence recorded in this plan's ledger) found the
correct, framework-native mechanism: `UIElement.AccessKeyScopeOwner` (confirmed real via
`Microsoft.WinUI.xml`), set on each item container, tells the framework those items belong to the
`ComboBox`'s scope even though they live in a `Popup` outside its visual tree (`IsAccessKeyScope`
alone does not cover `Popup` content). With it set, the framework correctly dismisses root-level
badges and requests only the item badges — confirmed live, working within milliseconds — but **only
from the second time a given `ComboBox`'s drop-down opens**. On the very first open, item requests
arrived roughly five seconds late, because the framework has not yet built its internal scope-tree
entry for containers it has never seen before.

**The fix:** use `AccessKeyScopeOwner` as the sole mechanism (delete the `Exit`/`Enter` pair
entirely — it is not needed once real scoping is in effect, and combining both would reintroduce the
this-task's-predecessor bug of root and item badges showing together). To close the first-open timing
gap, trigger one display-mode refresh — but only exactly when new containers were just wired this
cycle (tracked via the existing `wired` set, not a new "first open" flag), so the refresh fires
precisely when the framework has containers it has not registered a scope for yet, and never fires on
a later, already-registered open.

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs` in full — Task 10's `Exit`/
`Enter` pair is removed, one line is added inside the wiring loop, and the loop's trailing
`if (continueChord)` block's condition changes.

- [ ] **Step 2: Assign `AccessKeyScopeOwner` and track newly-wired containers**

Replace the body of the `for` loop and the trailing `if (continueChord)` block with:

```csharp
                var anyNewlyWired = false;
                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is not ComboBoxItem container)
                    {
                        continue;
                    }

                    container.AccessKey = (i + 1).ToString();
                    // Tells the framework these items belong to the ComboBox's access-key scope even
                    // though they live in a Popup, outside its visual tree -- IsAccessKeyScope alone
                    // does not cover Popup content. With this set, the framework correctly dismisses
                    // root-level badges and requests only these item badges on its own, no manual
                    // Exit/Enter needed (confirmed live -- see this plan's ledger for the spike that
                    // established this).
                    container.AccessKeyScopeOwner = owner;

                    if (!wired.Add(container))
                    {
                        continue;
                    }

                    anyNewlyWired = true;
                    badgeLayer.Watch(container);
                    container.AccessKeyInvoked += (_, args) =>
                    {
                        onActivated(container, owner.IndexFromContainer(container));
                        args.Handled = true;
                    };
                }

                if (continueChord && anyNewlyWired)
                {
                    // AccessKeyScopeOwner alone handles every open correctly EXCEPT the very first
                    // time a given container is realized -- the framework has not yet built an
                    // internal scope-tree entry for it, so its access key doesn't get asked to
                    // display for several seconds. Forcing one display-mode refresh here closes that
                    // gap, and ONLY here: gating on anyNewlyWired (not a separate "first open" flag)
                    // means this never fires on a later open of the same, already-registered
                    // containers, where the framework's own scoping already responds within
                    // milliseconds on its own -- confirmed live; doing this unconditionally on every
                    // open would re-introduce root and item badges showing together, since re-entering
                    // display mode at all re-triggers the framework's own scope evaluation from
                    // scratch each time, and that evaluation is fast enough on an already-registered
                    // container to look identical to not having done it -- but is not proven safe to
                    // repeat on every single open, so it is intentionally limited to exactly the
                    // condition that needs it.
                    AccessKeyManager.ExitDisplayMode();
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
```

- [ ] **Step 3: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "fix: use AccessKeyScopeOwner for correct scope replacement, nudging only on first realization"
```

---

### Task 12: Live verification

**Files:** none — this task runs the app, no code changes.

**Interfaces:** none.

This mechanism — real click-through, real z-order above a drop-down, real on-screen rendering —
cannot be verified by any automated test in this codebase, the same limitation this whole feature has
had throughout. This is the gate before considering the plan done.

- [ ] **Step 1: Launch the app**

```bash
pwsh -c "Get-Process WinTabberUI -ErrorAction SilentlyContinue | Stop-Process -Force; dotnet build winui3/WinTabberUI/WinTabberUI.csproj -p:Platform=x64"
pwsh -c "Start-Process (Get-ChildItem winui3/WinTabberUI/bin/x64/Debug -Recurse -Filter WinTabberUI.exe | Select-Object -First 1).FullName"
```

- [ ] **Step 2: Check all of the following, in the running app**

1. Press Alt — top-level badges (Prev/Play-Pause/Next, the three `ComboBox`es, the two
   `VolumeControls` elements) appear, correctly positioned over their own elements.
2. Press a `ComboBox`'s own access key — the drop-down opens, and item badges (`1`, `2`, `3`, ...)
   are visible **above** the drop-down's own content, on the first attempt, every time (not
   intermittently) — this is the exact failure this whole plan exists to fix.
3. Press a digit key — the correct item is selected, the drop-down closes.
4. Click somewhere in the app (or another app) while badges are showing — the click reaches whatever
   is underneath, not the overlay (confirms click-through actually works, not just "badges are
   visible").
5. Move the `MediaControlsWindow` to a different monitor (if more than one is available), then open
   the chord again — badges appear on the correct (new) monitor, not the old one.
6. Dismiss the chord (Escape or click away) — all badges disappear, and the overlay window itself is
   confirmed hidden (it should not appear in Alt-Tab or the taskbar at any point, including while
   showing badges — `IsShownInSwitchers = false` from Task 1).

Report the actual observed result for every check above — do not mark this task complete from code
reading alone. If any check fails, treat it as a normal bug: return to the relevant task's code with
fresh eyes rather than patching this task's own (empty) file list.

---

### Task 13: Close an open ComboBox drop-down when the owner window hides

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `public event Action? OwnerHidden;` on `AccessKeyBadgeLayer` — `DynamicAccessKeyScope`
  consumes it.

**Why this task exists:** user request after live verification passed — if a `ComboBox`'s drop-down
is open when the owning window is hidden (Task 9's owner-hide handling already clears badges and
hides the overlay in this case), the drop-down itself should also close, rather than staying open
underneath a hidden window.

**The fix:** `AccessKeyBadgeLayer` already owns the single subscription point for "the owner just
became hidden" (Task 9's `OnOwnerAppWindowChanged`). Expose that moment as a public event so other
code can react to it too, instead of adding a second, duplicate `AppWindow.Changed` subscription per
`ComboBox`.

- [ ] **Step 1: Read both files first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs` and
`winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs` in full.

- [ ] **Step 2: Add and raise the `OwnerHidden` event**

In `AccessKeyBadgeLayer.cs`, add a public event:

```csharp
    public event Action? OwnerHidden;
```

Raise it inside `OnOwnerAppWindowChanged`, after the existing early-return guard and before (or after
— order does not matter here) the badge-clearing logic:

```csharp
    private void OnOwnerAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange || sender.IsVisible)
        {
            return;
        }

        OwnerHidden?.Invoke();

        foreach (var badge in _badges.Values)
        {
            _overlay.Content.Children.Remove(badge.Visual);
        }
        _badges.Clear();
        _overlay.HideOverlay();
    }
```

- [ ] **Step 3: Subscribe from `DynamicAccessKeyScope`**

In `AttachSequentialKeys`, add one subscription near the existing `owner.AccessKeyInvoked` and
`owner.DropDownOpened` wiring:

```csharp
        badgeLayer.OwnerHidden += () => owner.IsDropDownOpen = false;
```

Setting `IsDropDownOpen = false` on an already-closed `ComboBox` is a harmless no-op — no additional
guard is needed.

- [ ] **Step 4: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 5: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass.

- [ ] **Step 6: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "feat: close an open ComboBox drop-down when the owner window hides"
```

---

### Task 14: Refuse to show a badge while the owner window is hidden

**Files:**
- Modify: `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no change to `AccessKeyBadgeLayer`'s public shape.

**Why this task exists:** live testing after Task 13 found that closing an open `ComboBox` drop-down
when the owner window hides (Task 13's own fix) can itself trigger the framework to re-request the
root-level access keys — landing after `OnOwnerAppWindowChanged`'s badge-clearing loop has already
run, so `_badges` gets repopulated with root badges and `ShowOverlay()` fires again, making root
badges appear right after everything was supposed to be hidden.

**The fix:** rather than chase the exact timing of whatever secondary request causes this (deferred,
asynchronous, and not worth pinning down precisely), make `OnAccessKeyDisplayRequested` refuse to act
on ANY request — from any source, at any time — while the owner window is not currently visible. This
closes the whole class of "something requested a badge while we're hidden" bugs, not just this one
specific trigger.

- [ ] **Step 1: Read the current file first**

Read `winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs` in full — only
`OnAccessKeyDisplayRequested` gains one early-return check at its top.

- [ ] **Step 2: Add the visibility guard**

Add, as the very first line of `OnAccessKeyDisplayRequested`, before the existing
`ComputeUnderlineLength` call:

```csharp
    private void OnAccessKeyDisplayRequested(UIElement sender, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs args)
    {
        if (!_owner.AppWindow.IsVisible)
        {
            // Refuse every request while the owner is hidden, regardless of what triggered it --
            // confirmed live: closing an open ComboBox drop-down as part of hiding (see Task 13) can
            // itself cause the framework to re-request root-level keys, arriving after the
            // owner-hidden badge-clearing pass already ran, which re-showed the overlay with root
            // badges right after everything was supposed to be gone. Checking IsVisible fresh here,
            // rather than a separate tracked flag, is always accurate and needs no explicit reset
            // when the owner becomes visible again.
            return;
        }

        var underlineLength = AccessKeyBadge.ComputeUnderlineLength(sender.AccessKey, args.PressedKeys);
```

- [ ] **Step 3: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Run the test suite**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -p:Platform=x64`
Expected: all tests still pass.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/AccessKeyBadgeLayer.cs
git commit -m "fix: refuse to show an access-key badge while the owner window is hidden"
```

---

### Task 15: Final live verification

- [ ] **Step 3: If all checks pass, remove the two abandoned design docs' "in progress" framing**

`docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md` already documents the two
prior failed designs in its own "Purpose" section — no edit needed there. No further cleanup step
exists for this task; this plan's earlier tasks already deleted every line of the abandoned
mechanisms as part of rebuilding the files that contained them (Tasks 3 and 4).

