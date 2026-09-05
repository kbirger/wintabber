# WinTabber.Api.Media.Tests

Deliberately narrow. Covered:

- `CoreAudioDeviceRepository`'s `HasDefaultAudioEndpoint` true/false paths, `EnumerateAudioEndPoints`-driven
  device population, state-change-driven add/remove (`OnDeviceAdded`/`OnDeviceStateChanged`), and
  its `CoreAudioDevicesMonitor` callback registration/unregistration and `Watch`'s volume/mute
  observables — all via `Fakes/FakeMMDeviceEnumeratorWrapper.cs` and `Fakes/FakeAudioDevice.cs`.
  `IAudioDevice` (see `docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md`)
  replaced `MMDevice` as the enumerator's return type specifically to make this possible.

**Not covered, and why:**

- `CoreAudioSessionRepository` entirely — its `AudioSessionManager` dependency (exposed via
  `IAudioDevice.AudioSessionManager`) is a separate COM-backed NAudio type with the same
  no-accessible-constructor problem `MMDevice` had. Out of scope for the `IAudioDevice` work;
  needs its own seam.
- `PolicyConfigClient` — raw `IPolicyConfig` COM interop, no seam, needs a real audio endpoint.
- `STAScheduler.Create()` — creates a real STA thread; not unit-testable, and not worth wrapping
  behind an interface just to verify "creates a thread." A consumer that needs to be testable
  should accept an `IScheduler` via constructor (as `CoreAudioDeviceRepository` already does) so
  a test can substitute `ImmediateScheduler`/`TestScheduler`.
- SMTC (`SMTCSessionRepository`/`SMTCSessionMonitor`/`SMTCSessionService`) and ShellApplications
  (`InstalledApplicationRepository`)'s WinRT/COM glue — no existing seam. Not manufactured
  speculatively (YAGNI) — a future task that wants coverage here needs its own design pass.

See `docs/superpowers/specs/2026-09-05-audio-device-abstraction-design.md` for the full reasoning
behind the `IAudioDevice` abstraction, and `docs/superpowers/specs/2026-09-04-phase-5-design.md`'s
T5.3 section for why this project started out this narrow.
