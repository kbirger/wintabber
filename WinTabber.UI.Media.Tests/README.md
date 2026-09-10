# WinTabber.UI.Media.Tests

TUnit. Currently one class: `AudioDeviceSelectorViewModelTests`, covering subscription
lifetime.

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

## Not covered

`MediaControlsViewModel` and `MediaSessionViewModel`. Both are constructible now that their
dependencies are interfaces, but neither is disposed by anything at runtime (see the remarks on
`MediaControlsViewModel.Dispose`), so a lifetime test would assert behaviour the application
never reaches. Worth revisiting together with that ownership fix.
