# Window selector — deferred items

Three findings from the `/simplify` review of the selection-jump fix (2026-08-27, branch
`audio`). All were deliberately skipped as out of scope for that change; each is
independently actionable. (A fourth finding, `SpatialNavigationListView._tileGrid` never being
invalidated, was fixed in `c80fb55` and removed from this list 2026-09-11.)

## 1. `OnActivated` re-runs the sizing that `ShowWindowSelector` just did

`WinTabberUI/Views/WindowSelectorWindow.xaml.cs` — `ShowWindowSelector()` calls `Activate()`,
which raises `OnActivated`, which calls `ScaleTiles()` and `CenterWindow()` again. The whole
point of `0f59bba` and the selection-jump fix was to get sizing done *before* `Show()`; these
calls quietly undo that guarantee.

Idempotent today, so nothing reflows. Skipped because removing them changes behaviour on
re-activation paths outside the reviewed diff — verify what else depends on re-centering when
the user clicks back onto an already-open selector before deleting.

## 2. ~~Duplicate "centre on the cursor's screen" logic~~ — DPI half fixed in `<pending>`

> **Resolved 2026-09-10 (partial).** The DPI-acquisition half of the duplication is gone:
> `WinTabberUI/Windowing/DesktopHelper.cs` now has a `ToLogicalBounds(this Visual, Rectangle)`
> extension that both windows call, always querying `VisualTreeHelper.GetDpi` live rather than
> `WindowSelectorWindow` caching it in `_dpiScale`. Deliberately **not** unified: `Screen.Bounds`
> vs `Screen.WorkingArea` — that's a real behaviour difference (can the selector overlap the
> taskbar?) the user chose to keep as two separate per-window decisions, not merge.
>
> Original description follows.

`WindowSelectorWindow.GetScreenBounds()` / `CenterWindow()` duplicate
`WinTabberUI/Views/SuspendedWindowsWindow.xaml.cs` (`PositionWindow`, ~line 49). The centering
expression is character-identical.

Not a straight extraction: the two disagree on `Screen.Bounds` vs `Screen.WorkingArea`, and take
DPI from different sources (`_dpiScale` vs `VisualTreeHelper.GetDpi(this)`). Reconciling those
is a behaviour decision for both windows, so this is a merge rather than a cleanup.

Natural home if done: `WinTabberUI/Windowing/DesktopHelper.cs`.

~~Related: `WinTabberUI/Services/UIScalingService.cs`...~~ — **deleted 2026-09-10.** Verified zero
references anywhere in the tree (Serena `find_referencing_symbols` on the class, not just grep);
it was never registered in `Bootstrapper.cs` and its `Dispose()` threw `NotImplementedException`.
The "become the shared helper or get deleted" choice resolved itself: `DesktopHelper` became the
shared helper for the DPI half of this item without needing anything from this class, so deletion
was the only remaining option.

The `Screen.Bounds` vs `Screen.WorkingArea` merge itself is **still open** — not touched by the
DPI fix above; kept as two separate per-window behaviors by explicit choice.

## 3. `HoverSelect` placement

`WinTabberUI/HoverSelect.cs` sits loose in the project root. The established home for attached
-property behaviours is `WinTabber.UI.Common/Behaviors/` (see `HintBehavior.cs`, same
`RegisterAttached` + static-accessor shape), and `WinTabber.UI.Media` has several `IsMouseOver`
triggers that cannot reach it from where it is.

Skipped because promoting it to the shared library implies reuse that does not exist yet — it
has exactly one consumer. Move it when a second one appears.
