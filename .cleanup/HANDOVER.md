# Handover note — 2026-09-10 (delete this file after reading)

## Where things stand

**`master` is at `v0.2.0` and released.** The `cleanup` branch (55 commits, Phases 0–5) was
fast-forwarded onto `master` — `master`'s history has no merge commits and stays linear — then
tagged `v0.2.0` and pushed. `.github/workflows/release.yml` fired and published the GitHub
Release with `WinTabber-0.2.0-win-x64.zip`. It went green.

**`testability` is the live branch**, 7 commits off `v0.2.0`, pushed. It holds the post-merge
bookkeeping and all of Phase 6.

`.cleanup/tasks.md` is accurate as of this note: **42 tasks, 41 done**. Every `[x]` really is
done and the one `[ ]` (T6.6) really is open. Build is warning-free; 109 tests pass, 107 under
the CI filter.

## Read in this order

1. [`tasks.md`](./tasks.md) — the Status block at the top, then Phase 6. Resolution notes under
   each task explain what was actually done, including where the task's own description was wrong.
2. [`WinTabber.UI.Common.Tests/README.md`](../WinTabber.UI.Common.Tests/README.md) — the two-tier
   WPF testing split and why it exists. Read before writing any WPF test.
3. [`WinTabber.UI.Media.Tests/README.md`](../WinTabber.UI.Media.Tests/README.md) — short; explains
   what T6.1 unlocked.

## What Phase 6 did

- **T6.1** — five interfaces extracted **verbatim** from existing public surface.
  `ICoreAudioDeviceRepository` *forwards* to the concrete registration
  (`sp => sp.GetRequiredService<CoreAudioDeviceRepository>()`) rather than re-registering: the
  type owns COM resources, so a second instance would be a defect, not just waste. The concrete
  registration survives because `AudioDeviceService` needs its `internal`
  `SetDefaultAudioEndpoint`, which was deliberately kept off the public interface.
- **T6.2** — `Ioc.Default` down from 16 call sites to 1 (`Bootstrapper.cs:35`, the composition
  root). `WindowSelectorWindowFactory` deleted.
- **T6.3** — subscription leaks fixed in `AudioDeviceSelectorViewModel`. The bad one was in a
  property setter, not the constructor, so it wanted a `SerialDisposable`, not a
  `CompositeDisposable`.
- **T6.4** — the static `_activeRootRef` became a replaceable `HintActivationScope`.
- **T6.5** *(new)* — a latent `NullReferenceException` in `HintBehavior.OnHintTextChanged`.
- **T6.6** *(new)* — **still open**, see below.

Two new test projects; the suite went 98 → 109.

## Open work

**T6.6 — the disposal ownership chain.** This is the one open task, and it is the reason T6.3 is
only half of its original title. Nothing disposes the view models, so the `Dispose` methods
Phase 6 added never actually run: `MediaControlsViewModel` is a DI singleton and `App.OnExit`
disposes only `BackgroundServiceContainer`, never the `ServiceProvider`. Closing it needs two
behaviour changes, which is why they were not folded into T6.3:

- dispose the `ServiceProvider` at shutdown — that disposes *every* singleton, including COM
  audio objects and input hooks, so it wants its own smoke test;
- restore `MediaControlsViewModel`'s commented-out `this.WhenActivated(` (`MediaControlsViewModel
  .cs:66`), which currently leaves a bare block and makes constructor-time subscriptions look
  activation-scoped when they are not.

**Three manual smoke tests, still never run by anyone.** These shipped in `v0.2.0` unverified —
a deliberate call, not an oversight. No session has had an interactive display or real audio
hardware. They need a human:

1. **Window blur/chrome** — `SetWindowCompositionAttribute` moved to
   `WinTabber.Interop/ChromeInterop.cs` in Phase 5A. Confirm blur-behind chrome still renders.
2. **Media/debug window ordering** — with the tray toggle on, confirm the debug window still
   appears correctly alongside the media controls window regardless of coordinator `.Init()`
   order.
3. **Default audio device switching + volume/mute** — `CoreAudioDeviceRepository
   .CreateDefaultDeviceChange` now eagerly builds a full `CoreAudioDevice` (two
   `AudioEndpointVolume` COM activations) where it once touched only `MMDevice.ID`. See that
   method's comment and the "Known deviation" section of
   [`2026-09-05-audio-device-abstraction-design.md`](../docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md).

**Not in `tasks.md` but open and adjacent:**
[`.todos/window-selector-cleanup.md`](../.todos/window-selector-cleanup.md) items 2–4 (item 1 was
fixed in `c80fb55`), and [`docs/testability-future-work.md`](../docs/testability-future-work.md).

**One unresolved unknown:** whether a CI runner tolerates `Window.Show()`. The desktop-tier tests
pass locally; `release.yml` excludes them rather than risking a failed *release* to find out.
Dropping the filter from the workflow is the experiment, and a non-release CI job would be the
safe place to run it.

## Things that cost time this session — don't rediscover them

**TUnit specifics.** `dotnet test --filter` does **not** work; it wants
`dotnet test <proj> -- --treenode-filter "/*/*/Class/*"`. Category filtering is
`"/*/*/*/*[Category!=RequiresDesktop]"` and it was verified in both directions. STA tests use
`[STAThreadExecutor]` from `TUnit.Core.Executors`. `HasCount(n)` is obsolete — use
`Count().IsEqualTo(n)`, which matters because this repo holds a 0-warning bar.

**WPF is more testable than it looks, in two tiers.** No `Application`, no `Dispatcher` and no
HWND are needed for attached properties, behavior attachment, or `DependencyProperty` change
callbacks — only an STA thread. A real `Window.Show()` *is* needed for anything touching the
visual tree or `IsLoaded`, because a `Window` has no visual child until it has a presentation
source; `Measure`/`Arrange` alone will not do it. Both halves were established by measurement,
not reasoning.

**`[Lazy]`-generated members are part of a type's public surface.** Reading a file for `public`
misses them. `MediaSessionService` declares *no* public members at all — its entire interface is
generated from private `GetXxx()` methods. An interface extracted from what the file literally
says would have been empty.

**`DisposeWith` lives in `System.Reactive.Disposables.Fluent`**, not
`System.Reactive.Disposables`.

**A clean build proves very little about DI here.** Nothing in the test suite touches the
container, a wrong constructor signature is a compile error but a missing registration is not,
and the six transient windows resolve lazily — so a gap would first appear when a user opened
that particular window. The technique that worked: add a temporary probe to
`BackgroundServiceContainer` resolving every registered window type, run the app, check the log,
revert. Worth repeating for any DI change.

## How to work here

**Verify, don't assert.** Three claims this session inherited or produced were wrong, and each
would have caused real damage if trusted: that T6.2 needed a design decision (the windows were
already container-built, so it was mechanical); that `HintBehavior` could not be tested (a
throwaway spike disproved it in minutes and turned up a live defect); and that
`MediaControlsViewModel` had adequate disposal (counting `DisposeWith` calls missed that nothing
disposed the composite). The cheap spike beat the confident inference every time. When a claim
gates a decision, measure it.

**A throwaway spike is the right tool for "can X even be done".** Five graduated probes, each
narrowing where things break, then delete the file. One probe "failing" is often the finding —
that is how T6.5 surfaced.

**Write tests that you have seen fail.** T6.3's leak tests were written after the fix, so the
fix was temporarily reverted to confirm they went red. They did; without that they would have
proved nothing.

**Serena's symbolic tools** for reference checks, renames and deletions — grep for discovery
only. `rename_symbol` is unreliable while the solution does not build.

**Commits.** One `feat:`/`chore:` per task when executing a plan; a `fix:` commit is fine for a
wave covering several findings. Commit messages here carry the *reasoning* — especially what
turned out to be false — because that is what the next session cannot re-derive. Don't commit
unless asked; this session committed throughout because it was explicitly asked to.

**Gate for every task:** `dotnet build WinTabber.slnx` at 0 warnings *and*
`dotnet test --solution WinTabber.slnx` green. For anything touching DI or windows, add the
45-second `dotnet run` smoke test — it catches what neither of those can.

`.cleanup/` is tracked, so anything added here lands in the repo. Delete this file once read.
