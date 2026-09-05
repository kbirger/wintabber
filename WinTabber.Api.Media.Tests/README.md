# WinTabber.Api.Media.Tests

Deliberately narrow. Covered:

- `CoreAudioDeviceRepository`'s `HasDefaultAudioEndpoint == false` path and its
  `CoreAudioDevicesMonitor` callback registration/unregistration, via
  `Fakes/FakeMMDeviceEnumeratorWrapper.cs`.

**Not covered, and why:**

- `CoreAudioDeviceRepository`/`CoreAudioSessionRepository`'s device-returning paths (the
  `HasDefaultAudioEndpoint == true` branch, `EnumerateAudioEndPoints`, `GetDevice`,
  `CoreAudioSessionRepository` entirely) — `IMMDeviceEnumeratorWrapper`'s device-returning
  members return NAudio's `MMDevice`, whose only constructor is `internal` and takes an internal
  COM interface. No test code can construct one. Making this testable needs a new abstraction
  over `MMDevice` — tracked as `.cleanup/tasks.md` T5.5, not in scope here.
- `PolicyConfigClient` — raw `IPolicyConfig` COM interop, no seam, needs a real audio endpoint.
- `STAScheduler.Create()` — creates a real STA thread; not unit-testable, and not worth wrapping
  behind an interface just to verify "creates a thread." A consumer that needs to be testable
  should accept an `IScheduler` via constructor (as `CoreAudioDeviceRepository` already does) so
  a test can substitute `ImmediateScheduler`/`TestScheduler`.
- SMTC (`SMTCSessionRepository`/`SMTCSessionMonitor`/`SMTCSessionService`) and ShellApplications
  (`InstalledApplicationRepository`)'s WinRT/COM glue — no existing seam. Not manufactured
  speculatively (YAGNI) — a future task that wants coverage here needs its own design pass.

See `docs/superpowers/specs/2026-09-04-phase-5-design.md`'s T5.3 section for the full reasoning.
