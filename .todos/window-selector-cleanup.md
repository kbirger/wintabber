# Window selector — deferred items

Four findings from the `/simplify` review of the selection-jump fix (2026-08-27, branch
`audio`). All were deliberately skipped as out of scope for that change; each is
independently actionable.

## 1. `SpatialNavigationListView._tileGrid` is never invalidated

`WinTabberUI/SpatialNavigationListView.cs` — `InitializeTileGrid()` builds `_tileGrid` on the
first arrow-key press and the field is never reset, so spatial navigation on every open after
the first works off a grid captured from a stale window list at stale tile positions.

Compounding it: `WindowSelectorWindow._tileGrid` is set to `null` in three places
(`SwitchWindowAndClose`, and both branches of `OnPreviewKeyDown`) but is *never* assigned
non-null — the window's own `InitializeTileGrid` is commented out. Those three lines look like
invalidation and do nothing.

Fix: delete the window's field and its three assignments; reset `_tileGrid = null` in
`SpatialNavigationListView` on `IsVisibleChanged` and on items change.

**This is a correctness bug, not a cleanup** — it was excluded because `/simplify` does not
hunt for bugs, not because it is low value.

## 2. `OnActivated` re-runs the sizing that `ShowWindowSelector` just did

`WinTabberUI/Views/WindowSelectorWindow.xaml.cs` — `ShowWindowSelector()` calls `Activate()`,
which raises `OnActivated`, which calls `ScaleTiles()` and `CenterWindow()` again. The whole
point of `0f59bba` and the selection-jump fix was to get sizing done *before* `Show()`; these
calls quietly undo that guarantee.

Idempotent today, so nothing reflows. Skipped because removing them changes behaviour on
re-activation paths outside the reviewed diff — verify what else depends on re-centering when
the user clicks back onto an already-open selector before deleting.

## 3. Duplicate "centre on the cursor's screen" logic

`WindowSelectorWindow.GetScreenBounds()` / `CenterWindow()` duplicate
`WinTabberUI/Views/SuspendedWindowsWindow.xaml.cs` (`PositionWindow`, ~line 49). The centering
expression is character-identical.

Not a straight extraction: the two disagree on `Screen.Bounds` vs `Screen.WorkingArea`, and take
DPI from different sources (`_dpiScale` vs `VisualTreeHelper.GetDpi(this)`). Reconciling those
is a behaviour decision for both windows, so this is a merge rather than a cleanup.

Natural home if done: `WinTabberUI/Windowing/DesktopHelper.cs`.

Related: `WinTabberUI/Services/UIScalingService.cs` already has `GetCursorScreen`,
`GetDeviceCenterScreen`, `GetCurrentScreenSize(Window)` — but it is registered nowhere, has zero
references, and its `Dispose` throws `NotImplementedException`. Either it becomes the shared
helper here or it should be deleted; leaving it dead next to hand-rolled equivalents is the
worst of both.

## 4. `HoverSelect` placement

`WinTabberUI/HoverSelect.cs` sits loose in the project root. The established home for attached
-property behaviours is `WinTabber.UI.Common/Behaviors/` (see `HintBehavior.cs`, same
`RegisterAttached` + static-accessor shape), and `WinTabber.UI.Media` has several `IsMouseOver`
triggers that cannot reach it from where it is.

Skipped because promoting it to the shared library implies reuse that does not exist yet — it
has exactly one consumer. Move it when a second one appears.
