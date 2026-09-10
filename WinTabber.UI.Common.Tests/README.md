# WinTabber.UI.Common.Tests

TUnit. Covers `WinTabber.UI.Common`'s hint system — the part of it that does not need a
compositor.

## Why this project exists at all

The prevailing assumption before it was written was that `HintBehavior` is untestable, in the
same category as `UacHelper` (needs a real Win32 token) or the DWM code (needs a live
compositor). That turned out to be wrong, and it was worth measuring rather than assuming.
`HintBehavior` touches no `Application.Current`, no `Dispatcher`, no `PresentationSource` and no
HWND. Its core is attached `DependencyProperty` registration plus change callbacks, which run
synchronously and in memory.

The one real requirement is an STA thread, and TUnit supplies one with
`[STAThreadExecutor]`.

## The two tiers

**Tier 1 — `HintBehaviorTests`.** No window is ever shown. Covers the activation-scope
arbitration (which root owns the hint overlay), and the null-window guard in
`OnHintTextChanged`. Runs anywhere an STA thread exists.

**Tier 2 — `HintBehaviorDesktopTests`, tagged `[Category("RequiresDesktop")]`.** Needs a real
`Window.Show()`. Two independent things force this, both measured:

- a `Window` has no visual child until it has a presentation source, so the visual-tree walk in
  `HintBehavior.GetAttachedElements` finds nothing from an unshown window regardless of how it
  is measured and arranged;
- `DefaultHintBehaviorKernel.GetAttachableElements` filters on `FrameworkElement.IsLoaded`,
  which stays false until the same thing happens.

Run everything:

```bash
dotnet test WinTabber.UI.Common.Tests/WinTabber.UI.Common.Tests.csproj
```

Skip the ones needing a desktop (this is what `.github/workflows/release.yml` does):

```bash
dotnet test WinTabber.UI.Common.Tests/WinTabber.UI.Common.Tests.csproj \
  -- --treenode-filter "/*/*/*/*[Category!=RequiresDesktop]"
```

Tier 2 passes locally. Whether a CI runner tolerates `Show()` is unverified, so CI excludes it
rather than risking a failed release to find out — drop the filter from the workflow to change
that.

## Notes

- Both classes are `[NotInParallel]`. `HintBehavior.ActivationScope` is a single shared seam;
  running these concurrently would reintroduce exactly the cross-test interference the scope was
  introduced to remove.
- `WinTabber.UI.Common` grants this project `InternalsVisibleTo` so tests can swap
  `HintBehavior.ActivationScope`. That seam is deliberately `internal` — it is a test affordance,
  not public API.

## Not covered

Adorner rendering and positioning, `HintAdorner` visuals, and the key-chord input path
(`OnTriggerKeyDown`) — all of which need either pixel output or synthesized input. Not attempted
here.
