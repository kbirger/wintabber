# WinTabber.UI.Media.Tests

TUnit. Two classes: `AudioDeviceSelectorViewModelTests` and `MediaControlsViewModelTests`, both
covering subscription lifetime.

## Why it can exist now

It could not before T6.1. `AudioDeviceSelectorViewModel` takes an audio device service, and
until that was extracted to `IAudioDeviceService` the only thing satisfying the constructor was
the real `AudioDeviceService`, which reaches COM. The interface is what makes
`Fakes/FakeAudioDeviceService.cs` possible, and therefore these tests.

The fake counts *live* subscriptions rather than recording calls, which is what lets a test
assert that something was released rather than merely created. Members the view model does not
use throw `NotSupportedException` on purpose: a future call site that starts depending on one
fails loudly here rather than quietly receiving a default.

## Notes

- `[NotInParallel]`: `RxApp.MainThreadScheduler` is global state and the view model observes on
  it. Tests set it to `CurrentThreadScheduler` since there is no dispatcher.

## MediaControlsViewModelTests

Added by T6.6, once `MediaControlsViewModel`'s commented-out `WhenActivated` was restored (see
`.cleanup/tasks.md`). Before that fix, everything in the constructor ran eagerly at construction;
these tests assert the opposite — nothing observable happens until `Activator.Activate()`, and
deactivation actually releases what activation created (`Playback`/`Recording`/`ActiveSession`),
rather than leaking on every reactivation. That last point matters because `MediaControlsWindow`
now activates the view model on every show and deactivates it on every hide (T6.6 also fixed the
window, which previously only ever deactivated), so the reactivation path is a real one, not a
hypothetical.

`FakeAudioDeviceService` gained a fourth supported member, `WatchDevice(IAudioDevice?)`, for
`MediaSessionViewModel` (constructed here via the real `MediaSessionViewModelFactory` — that
factory isn't behind an interface, so there's no seam to fake it out from under). Everything
`MediaSessionViewModel` actually reads from its `Session` is unset in these tests (no
`AggregateSession` is ever added — its own constructor takes a real WinRT session type this test
project cannot build), so the DTO the fake returns is inert by construction, not selectively
stubbed.

## Not covered

`MediaSessionViewModel` on its own — only indirectly, via `MediaControlsViewModel`. It has no
disposal or lifetime test of its own.
