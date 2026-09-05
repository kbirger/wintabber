# Handoff note (delete this file after reading)

Branch `cleanup` (off `master`). Phases 0-4 were done in earlier sessions. **Phase 5 (both
5A and 5B) is now done, reviewed, and fixed** — last commit is `19a3dfb` ("fix: address final
whole-branch review findings (C1, I1, I2)"), preceded by `7528fb1` (a spec/doc correction).
The branch is being **kept as-is, not merged or pushed** — that was an explicit choice, not an
oversight; pick up from here when ready to merge/PR/continue.

Read in this order before touching anything:
1. [`docs/superpowers/specs/2026-09-04-phase-5-design.md`](../docs/superpowers/specs/2026-09-04-phase-5-design.md) — the design, including two corrections made mid-implementation (see below)
2. [`docs/superpowers/plans/2026-09-04-phase-5a-interop-split.md`](../docs/superpowers/plans/2026-09-04-phase-5a-interop-split.md) — 11 tasks, all complete
3. [`docs/superpowers/plans/2026-09-04-phase-5b-test-coverage.md`](../docs/superpowers/plans/2026-09-04-phase-5b-test-coverage.md) — 5 tasks, all complete
4. [`tasks.md`](./tasks.md) — has a resolution note under every completed task; T5.5 and T5.6 are new, not yet planned

## What Phase 5 did

- Split `WinTabber.Interop/IInteropProxy.cs` (39-member interface) into four consumer-driven
  interfaces: `IProcessControl` (6), `IWindowVisibility` (2), `IWindowPlacement` (5),
  `IWindowInterop` (26 own + `IWindowVisibility` = 28). Every consumer narrowed to what it
  actually uses; the Suspension test fake shrank from 39 stubbed members (31 throwing) to 8 real
  ones (0 throwing).
- Deleted a dead `Bitmap2BitmapImage`/`DeleteObject` method (only comment-only call sites),
  migrated the one live copy plus `UacHelper`'s token-elevation check to CsWin32, and relocated
  `SetWindowCompositionAttribute` out of `WinTabber.UI.Common/Chrome` into `WinTabber.Interop`
  (policy correction: hand-written declarations for undocumented APIs live in `WinTabber.Interop`
  regardless of what they act on — CLAUDE.md's Windows Interop section now says this).
- Made two `BackgroundServiceContainer` ordering hazards structural instead of comment-enforced:
  a `ShownChanges` observable on `ViewCoordinatorBase<T>` so `MediaDebugWindowCoordinator` reacts
  to `MediaWindowViewCoordinator`'s actual shown state, and `EnableDebugPrivilege()` moved into
  `ProcessSuspensionService`'s own constructor.
- Added `WinTabber.Api.Media.Tests` and `WinTabber.Interop.Tests` (previously zero coverage on
  both projects). Solution-wide test count: 81 → 92. Both new projects have a `README.md`
  documenting what's deliberately *not* covered and why — read those before assuming a gap is an
  oversight.
- Build: 0 warnings, 0 errors. Tests: 92/92 passing. Verified independently by the controller
  session, not just trusted from subagent reports.

## Two things a human still needs to check before this ships

Neither implementer had an interactive display available, so these are compile-verified and
behaviorally-reasoned-through but **not visually confirmed**:

1. **Window blur/chrome rendering** — `SetWindowCompositionAttribute` moved projects
   (`WinTabber.UI.Common/Chrome/Interop.cs` → `WinTabber.Interop/ChromeInterop.cs`). Launch the
   app and confirm any window using blur-behind chrome (`grep -rn "AccentHelper.EnableBlur"` to
   find which) still renders correctly.
2. **Media controls / debug window show ordering** — confirm the debug window still appears
   correctly alongside the media controls window when the tray toggle is on. The whole point of
   T5.2's fix was to make this order-independent; worth actually toggling the two coordinators'
   `.Init()` order in `BackgroundServiceContainer.cs` (temporarily) and confirming behavior is
   unchanged either way, then reverting.

## Corrections made mid-implementation (read before trusting the original spec text)

- **T3.4's "duplicate `DeleteObject`" claim was wrong.** `InstalledApplicationRepository.cs`'s
  copy was dead code (comment-only call sites) — deleted outright, not deduplicated. Only
  `AppCache.cs`'s copy was live, migrated to CsWin32 in place.
- **The `IInteropProxy` split needed a 4th interface, not 3.** `IWindowVisibility` was carved out
  of what would otherwise have been a 34-member fake (`ProcessSuspensionService` needing
  `HideWindow`/`RestoreWindow` from an otherwise-28-member `IWindowInterop`) — without it, the
  whole "shrink the fake" goal would have been mostly unmet.
- **T5.3's scope was cut down hard.** `IMMDeviceEnumeratorWrapper`'s device-returning members
  (`GetDefaultAudioEndpoint`, `EnumerateAudioEndPoints`, `GetDevice`) all return NAudio's
  `MMDevice`, whose only constructor is `internal` — confirmed via reflection against the actual
  DLL. No test code can fake those members meaningfully. Only `HasDefaultAudioEndpoint` and the
  two `*EndpointNotificationCallback` methods are covered; the real fix (a new `IAudioDevice`
  abstraction) is tracked as **T5.5**, not scoped or started.
- **The spec's claim that `UacHelper.IsProcessElevated(int)`/`(Process)` were "near-verbatim
  duplicates" was wrong**, caught in the final whole-branch review. The `int` overload ignores
  its parameter entirely and always reports WinTabber's own elevation — a live, pre-existing bug
  in `InteropProxy.BringWindowToFront`, correctly preserved as-is by the CsWin32 migration (right
  call for a mechanical migration; fixing the ignored parameter is a real behavior change).
  Tracked as **T5.6**, not fixed in this branch.

## Traps that cost time in Phase 5 (in addition to the Phase 1-4 list already in this file's history)

- **Serena's `rename_symbol` doesn't work reliably while the solution doesn't build.** During
  T5.1's Task 3 (mid-interface-split, solution intentionally broken), `rename_symbol` renamed only
  the class declaration and missed every reference site — likely cross-project LSP resolution
  breaks when the solution won't compile. Fell back to manual edits + grep verification. If a
  future task needs `rename_symbol` while the solution is mid-refactor and broken, expect this and
  verify with grep regardless of what the tool reports.
- **A per-task, diff-scoped review cannot catch a defect that spans two tasks.** The final
  whole-branch review caught a Critical bug (`IWindowVisibility` never registered in DI — the app
  would have crashed on startup) that both individual task reviews (DI registration in one task,
  the new constructor dependency in a later task) missed, because neither had both halves in view.
  Confirmed the final whole-branch review step is not optional ceremony — it found something real
  that 16 clean per-task reviews and a green build/test suite both missed.
- **A green build and passing tests do not prove DI resolves at runtime.** `Microsoft.Extensions.DependencyInjection`
  resolution failures are runtime errors; nothing in this repo's test suite constructs the DI
  container (tests build services by hand), so a DI registration gap is invisible to `dotnet build`
  and `dotnet test` alike. The only way the final review caught it was reasoning through the
  registration graph and reproducing the resolution in an isolated scratch project.

## What's left

- **T5.5** — design an `IAudioDevice`-style abstraction over NAudio's `MMDevice` so
  `CoreAudioDeviceRepository`'s device-returning logic becomes testable. Needs its own
  brainstorming pass (ripples into `CoreAudioDeviceWrapper` and other `MMDevice` consumers) —
  don't just start implementing.
- **T5.6** — fix `UacHelper.IsProcessElevated(int)` ignoring its parameter. Needs its own review
  and smoke test since it changes `InteropProxy.BringWindowToFront`'s actual behavior.
- **Phase 6** — already tracked in `docs/testability-future-work.md`; `tasks.md` only lists it for
  completeness.

## Working preferences (carried over, still true)

Use Serena's symbolic tools for renaming/deleting/checking usage — with the new caveat above about
`rename_symbol` during a broken-build window. Commit style: one `chore: Phase N T<x> — ...` commit
per task when executing a plan via subagent-driven-development; the two exceptions this round were
the fix-wave commit (`fix: ...`, covers three findings at once) and the doc-correction commit
(`docs: ...`). Don't commit unless asked — this session committed throughout because it was
explicitly running an approved plan via subagent-driven-development, not by default.

`.cleanup/` is tracked and committed, so anything added here lands in the repo.

Delete this file once you've read it.
