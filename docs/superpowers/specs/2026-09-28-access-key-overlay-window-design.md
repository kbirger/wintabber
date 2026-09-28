# Access-Key Overlay Window Design

## Purpose

Custom access-key badges (`AccessKeyBadge`, `AccessKeyBadgeLayer`) must draw above everything in
their owning window, including an open `ComboBox` drop-down. Two prior designs failed this:

1. Badges as `Canvas` children of an in-window overlay — a `Canvas` can never draw above any
   `Popup`, drop-downs included. This was the original bug report.
2. Badges as their own sibling `Popup` — works for static badges (a plain `Popup` opened later
   draws above one opened earlier), but a `ComboBox` drop-down is a *light-dismiss* `Popup`, which
   WinUI keeps on a stacking layer above every ordinary `Popup` regardless of open order. No sibling
   `Popup` can ever win against it. For item badges specifically, a follow-up design injected badges
   directly into the drop-down's own internal content panel — this worked in log-verified logic
   every time, but rendered inconsistently live (see
   `docs/superpowers/plans/2026-09-27-access-key-popup-zorder-fix.md`'s ledger for the full history):
   injecting foreign children into a `ComboBox`'s internal, undocumented panel is not a supported
   operation, and WinUI's own virtualizing/arrange logic for that panel does not reliably account for
   them.

This document replaces both: badges draw in a **separate, always-on-top OS window**, entirely
outside WinUI's own popup-stacking rules. A separate HWND's z-order is controlled by DWM/the window
manager, not by WinUI's internal popup layering, so it draws above a `ComboBox` drop-down (or
anything else in the owning window) unconditionally.

## Architecture

One new type, `AccessKeyOverlayWindow`, plus a rebuild of `AccessKeyBadgeLayer` around it.
`DynamicAccessKeyScope` simplifies back down to the shape it had before either popup-based attempt.

`AccessKeyOverlayWindow` is a chromeless `WindowEx` (WinUIEx, the same base every other window in
`winui3/WinTabberUI/Views/` uses):

- **No title bar, no border, no taskbar entry, never activates.**
- **Sized and positioned to cover exactly the monitor its owning window currently occupies** — not
  the owning window's own bounds, and not the whole virtual desktop across every monitor. Every
  hinted element lives inside one window, on one monitor at a time; covering more buys nothing and
  costs per-monitor-DPI complexity for no reason.
- **Transparent everywhere except badge visuals.**
- **Click-through**: `WS_EX_TRANSPARENT` on the HWND's extended style, so every mouse event passes
  through to whatever is beneath it, even while it visually covers the whole monitor. Combined with
  `WS_EX_LAYERED` (required for `WS_EX_TRANSPARENT` to take effect) and `WS_EX_NOACTIVATE` (so
  showing it never steals focus).
- **Kept above its owning window in z-order** by being set as an *owned* window of the owner's HWND
  (`GWLP_HWNDPARENT` / `SetWindowLongPtr`, the standard Win32 owned-window relationship — not
  `WS_CHILD`, and not global "always on top," which would float above unrelated apps too). An owned
  window is always kept above its owner by the window manager, which is exactly the guarantee this
  design needs and nothing more.
- **Created once, per owning window, at that window's construction. Shown and hidden thereafter —
  never recreated per chord.**

`AccessKeyBadgeLayer` is rebuilt to own one `AccessKeyOverlayWindow` instead of managing a `Canvas`
and a dictionary of `Popup`s. Its public shape is unchanged: `Watch(UIElement element)` still
subscribes to `AccessKeyDisplayRequested`/`Dismissed`; callers (including `DynamicAccessKeyScope`,
for `ComboBoxItem`s) call it exactly as they call it for any static element — **no special case for
`ComboBox` items anymore.**

This deletes, outright, the entire mechanism the prior two designs built:
`SnapshotOpenPopups`, `InjectIntoNewPopup`, `AddItemBadge`, `FindNewPopup<T>`, and the
per-badge-`Popup` construction in `OnAccessKeyDisplayRequested`/`Dismissed`.

## Who creates the overlay, and for which window

Exactly as today: each hinted window explicitly constructs its own `AccessKeyBadgeLayer` in its own
code-behind. `MediaControlsWindow.xaml.cs:86` currently does
`new AccessKeyBadgeLayer(AccessKeyOverlay)`, passing its own `Canvas`. Under this design it becomes
`new AccessKeyBadgeLayer(this)`, passing the owning `WindowEx` itself. `AccessKeyBadgeLayer`'s
constructor creates the `AccessKeyOverlayWindow` from that owner, using the owner's `AppWindow`/HWND
both to establish the owned-window z-order relationship and to compute which monitor to cover.

This is not a global or automatic mechanism — nothing decides "this window gets hints" except the
window's own code choosing to construct an `AccessKeyBadgeLayer`, exactly as it does today. The
`AccessKeyOverlay` `Canvas` in each hinted window's XAML (e.g. `MediaControlsWindow.xaml:343`)
becomes unnecessary and is removed.

## Coordinate placement

An element's position is read in its own window's coordinates exactly as before
(`element.TransformToVisual(null).TransformPoint(new Point(0, 0))`), then converted into the
overlay's own coordinate space:

```
overlayLocal = elementLocal + (ownerWindow.AppWindow.Position - overlayWindow.AppWindow.Position) / scale
```

`AppWindow.Position` is each window's top-left in physical screen pixels; `scale` is the shared DPI
scale for the monitor both windows are on (`XamlRoot.RasterizationScale`, identical for both windows
since the overlay always covers the owner's own monitor). Because both `Position` values are already
in the same physical-pixel space, this reduces to a constant per-monitor offset, recomputed once each
time the overlay transitions from hidden to shown (not on every layout pass, and not by continuously
tracking the owner's move events) — cheap, and sufficient: badges are only ever positioned while a
chord is actively displaying, which starts from a hidden overlay every time.

The arithmetic above is pulled into one small static method
(a natural home: `AccessKeyOverlayWindow` itself, or a private helper `AccessKeyBadgeLayer` calls),
unit-testable the same way `AccessKeyBadge.ComputeUnderlineLength` already is — it takes two
`PointInt32`/`Point` positions and a scale, and returns a `Point`. No WinUI type construction is
needed to test it, avoiding the headless-construction limitation this codebase has already hit twice
(`WinTabber.UI.Common.Tests`' inability to construct a real `Popup` — see
`docs/superpowers/plans/2026-09-27-access-key-popup-zorder-fix.md`'s Task 1).

## Data flow

- **Startup:** the owning window constructs its `AccessKeyBadgeLayer(this)`; the layer constructs a
  hidden `AccessKeyOverlayWindow` anchored to the owner.
- **`Watch(element)`:** unchanged — subscribes to the element's `AccessKeyDisplayRequested`/
  `Dismissed`, for a static element or a dynamically-keyed `ComboBoxItem` alike.
- **`AccessKeyDisplayRequested`:** compute the element's overlay-local position (above); show the
  overlay window if it is not already showing; add or update the badge's `Canvas` position inside
  the overlay's content; set the underline prefix exactly as today.
- **`AccessKeyDisplayDismissed`:** remove that badge from the overlay's `Canvas`; once no badges
  remain, hide the overlay window (hide, not close — it is reused for the next chord).
- **Owner window moves or changes monitor while the overlay is hidden:** nothing to do — the
  overlay's bounds are recomputed the next time it is shown.
- **Owner window moves while the overlay is currently showing** (e.g. the user drags the window
  mid-chord): out of scope for this design's first version — badges may render at a stale position
  until the next hide/show cycle. Note this as a known, accepted limitation; revisit only if it
  proves to matter in practice.

## Windows interop

Per this codebase's own architecture rule (`CLAUDE.md`): CsWin32-backed Win32 that affects *our own*
window's rendering or behavior lives beside the WinUI code that owns it, not in `WinTabber.Interop`
(reserved for Win32 that observes or mutates *another* process's windows). `AccessKeyOverlayWindow`'s
interop — `SetWindowLongPtr`/`GetWindowLongPtr` for `WS_EX_LAYERED`/`WS_EX_TRANSPARENT`/
`WS_EX_NOACTIVATE`, and the `GWLP_HWNDPARENT` owned-window relationship — is exactly this case: it
affects only how our own overlay window behaves, never another process's window.

This interop lives in `winui3/WinTabber.UI.Common/AccessKeys/`, alongside `AccessKeyOverlayWindow`
and `AccessKeyBadgeLayer`, with its own `NativeMethods.txt` in that project (mirroring the pattern
`WinTabber.UI.Common/NativeMethods.txt` already establishes on the WPF side for
`DwmSetWindowAttribute`). This is the first use of CsWin32 anywhere under `winui3/` — confirmed by
searching the tree for any existing `NativeMethods.txt` there (none exists) — so
`winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj` needs the `Microsoft.Windows.CsWin32`
package reference added (the WPF-side `WinTabber.UI.Common.csproj` and `WinTabberUI/WinTabberUI.csproj`
already reference it; the version is already pinned centrally, so this is an ordinary
`<PackageReference Include="Microsoft.Windows.CsWin32" />` line, no new version to pick).

## Error handling

- If setting the click-through/owned-window extended styles fails (a `SetWindowLongPtr` call
  returning an error), log it and continue rather than throwing — the overlay still shows badges,
  just without the click-through guarantee. This matches the codebase's existing recoverable-failure
  pattern (`AccessKeyBadge.TryGetResource`'s themed-resource fallback).
- If the owner window's monitor cannot be determined (`DisplayArea` lookup fails — a theoretical
  edge case with no known repro), skip showing that request's badge and log it, rather than throwing
  out of an event handler.

## Testing

- The coordinate-transform helper is a plain static method over primitive/point types and is
  unit-tested directly, without constructing any WinUI or Win32 window type.
- `AccessKeyOverlayWindow`'s actual click-through behavior, actual z-order above a drop-down, and
  actual on-screen rendering cannot be verified by any automated test in this codebase — the same
  limitation every part of this feature has run into. Live verification, running the real app,
  remains the gate before any task implementing this design is considered done.

## Migration from the current (popup-based) implementation

The current committed state (see `docs/superpowers/plans/2026-09-27-access-key-popup-zorder-fix.md`)
has:

- A working static-badge `Popup`-per-badge mechanism (this design's `AccessKeyOverlayWindow`
  replaces it outright — the `Popup`-per-badge code is deleted, not kept as a fallback).
- A working Alt-toggle chord-continuation fix in `DynamicAccessKeyScope`
  (`openedByAccessKey`/`continueChord`) — **this stays. It is orthogonal to how badges are drawn.**
- A non-working item-badge-in-drop-down-popup mechanism (`SnapshotOpenPopups`/`InjectIntoNewPopup`/
  `AddItemBadge`/`FindNewPopup<T>`) — **deleted outright** by this design; `DynamicAccessKeyScope`
  goes back to calling `badgeLayer.Watch(container)` for items, identical to any static element.

## Explicit exclusions

- No support for the overlay repositioning live while visible during an owner-window drag (see Data
  flow above) — accepted limitation for this version.
- No multi-monitor spanning — the overlay only ever covers the owner window's current monitor.
- No support for a hinted window that has no monitor at all (e.g. fully off-screen) — undefined
  behavior, not handled specially.
