# Handover note (delete this file after reading)

Branch `cleanup` (off `master`). Everything through Phase 5 (0-4, 5A, 5B) plus **T5.5 is now
done, reviewed, and merged onto this branch** — last commit is `bea4c97` ("chore: tasks.md
bookkeeping — check off T5.1-T5.4"), which itself just closes a stale-checkbox gap left over
from Phase 5A/5B (no new work in that commit). The branch is being **kept as-is, not merged or
pushed** — an explicit choice, not an oversight. `.cleanup/tasks.md` is now accurate: every
`[x]` there really is done, every `[ ]` really is open.

Read in this order before touching anything:
1. [`docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md`](../docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md) — T5.5's design, including a documented "Known deviation" section
2. [`docs/superpowers/plans/2026-09-05-audio-device-abstraction.md`](../docs/superpowers/plans/2026-09-05-audio-device-abstraction.md) — 7 tasks, all complete
3. [`tasks.md`](./tasks.md) — T5.1-T5.5 all `[x]` with resolution notes; everything still `[ ]` is genuinely open

## What T5.5 did

- Added `IAudioDevice` (`WinTabber.Api.Media/CoreAudio/IAudioDevice.cs`) to replace NAudio's
  `MMDevice` — internal constructor, uninstantiable by test code — as the type flowing through
  the enumerator seam and every consumer in `WinTabber.Api.Media`.
- Renamed `CoreAudioDeviceWrapper` → `CoreAudioDevice` (its real, COM-backed implementation).
  Added `FakeAudioDevice` (freely constructible) and rewrote `FakeMMDeviceEnumeratorWrapper` so
  its three previously-`NotSupportedException`-throwing device-returning members are now real
  fakes.
- `MMDevice` now appears nowhere in the solution except `MMDeviceEnumeratorWrapper.cs` and
  `CoreAudioDevice.cs` (plus a handful of doc-comment mentions and pre-existing commented-out
  dead code deliberately left alone).
- Deleted two confirmed-dead code paths: `AudioDeviceService.CanSetVolume(MMDevice)`/
  `CanMute(MMDevice)`, and `AudioDeviceSelectorViewModel.GetDevicesObservable(MMDeviceEnumerator)`.
- Added 6 new tests covering `CoreAudioDeviceRepository`'s previously-untestable device-returning
  paths and `CoreAudioDevicesMonitor.Watch`'s volume/mute observables. Test count: 92 → 98.
- Build: 0 warnings, 0 errors. Tests: 98/98 passing. Went through per-task review on all 7 tasks
  plus a final whole-branch review (Opus) — one Important finding fixed (see "Known deviation"
  below), several Minors deliberately parked as future-coverage debt (see the design spec / final
  review — not repeated here since they're not urgent).

## One thing a human still needs to check before this ships

Same situation as the Phase 5 handoff before it: no interactive display or real audio hardware
was available to this session. Three items, combined into one checklist (the first two carried
over unverified from Phase 5A, the third is new from T5.5):

1. **Window blur/chrome rendering** — `SetWindowCompositionAttribute` moved from
   `WinTabber.UI.Common/Chrome/Interop.cs` to `WinTabber.Interop/ChromeInterop.cs` back in Phase
   5A. Launch the app and confirm any window using blur-behind chrome still renders correctly.
2. **Media controls / debug window show ordering** — confirm the debug window still appears
   correctly alongside the media controls window when the tray toggle is on, regardless of which
   coordinator's `.Init()` runs first in `BackgroundServiceContainer.cs`.
3. **Default audio device switching + volume/mute (new)** — launch the app, open the audio device
   selector, switch the default playback/recording device, and adjust volume/mute once each.
   `CoreAudioDeviceRepository.CreateDefaultDeviceChange` now eagerly constructs a full
   `CoreAudioDevice` (including two `AudioEndpointVolume` COM activations) where it used to touch
   only `MMDevice.ID` — see that method's code comment and the design spec's "Known deviation"
   section for exactly what could go wrong and why it was judged low-probability enough not to
   block on.

## What's left (to discuss and prioritize in a new session — not started, not scoped yet)

- **T5.6** — `UacHelper.IsProcessElevated(int processId)` ignores its parameter and always
  reports WinTabber's own elevation state, not the target process's.
  `InteropProxy.BringWindowToFront` calls this overload with the *target* window's process id, so
  it's silently checking the wrong thing. A real behavior change, not a dedup/test-coverage task —
  needs its own review and smoke test.
- **T4.6** — Consider thinning `WinTabberUI`'s root (18 loose top-level files contributing to a
  6/10 cohesion score).
- **Phase 6** (canonical description in `docs/testability-future-work.md`; all four confirmed
  still open as of `af16e91`):
  - **T6.1** — Add interfaces for the 5 concrete media-service DI registrations in
    `Bootstrapper.cs` (`CoreAudioDeviceRepository`, `AudioSessionService`, `AudioDeviceService`,
    `MediaSessionService`, `InstalledApplicationRepository`).
  - **T6.2** — Restrict `Ioc.Default` to startup; use constructor injection instead (17 call sites
    across `WinTabberUI` and `WinTabber.UI.Media`).
  - **T6.3** — Move constructor-time Rx subscriptions to `WhenActivated`/`Initialize()`.
    `AudioDeviceSelectorViewModel` (lines 57/61/100 as of this writing) still subscribes in the
    constructor with no `CompositeDisposable`; `MediaControlsViewModel` has partially adopted
    `.DisposeWith(_cleanUp)`.
  - **T6.4** — Fix a `static WeakReference<FrameworkElement>?` at
    `WinTabber.UI.Common/Behaviors/HintBehavior.cs:161` shared across test runs.

None of these have been brainstormed or planned yet — the next session should start by
discussing which one(s) to tackle and in what order, not by jumping into implementation.

## Working preferences (carried over, still true)

Use Serena's symbolic tools for renaming/deleting/checking usage — with the caveat from the
previous handoff about `rename_symbol` being unreliable while the solution doesn't build (this
session avoided it entirely for exactly that reason, doing renames as manual create/delete
instead). Commit style: one `chore: T<x> N — ...` commit per task when executing a plan via
subagent-driven-development (this session used `chore: T5.5 N — ...`); a `fix: ...` commit is
fine for a fix wave that covers several findings at once. Don't commit unless asked — this
session committed throughout because it was explicitly running an approved plan via
subagent-driven-development, not by default.

For subagent-driven-development specifically: dispatch implementers on the cheapest model that
fits the task (mechanical/transcription-level tasks → haiku; multi-file integration or anything
needing judgment → sonnet), and dispatch the final whole-branch review on the most capable model
available (this session used opus) — it's the one review pass with the budget to catch
cross-task integration issues a per-task review structurally cannot see. If the branch you're
executing on is a long-lived branch rather than a fresh worktree, scope the final review's
`MERGE_BASE` to where *this plan's* work started, not `git merge-base master HEAD` — the latter
drags in every already-reviewed commit from earlier, unrelated phases.

`.cleanup/` is tracked and committed, so anything added here lands in the repo.

Delete this file once you've read it.
