# InstalledApplicationRepository gives no signal on shell-acquisition failure

`WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs`'s
`ApplicationsByAumid`/`ApplicationsByPath` are built via DynamicData `.Or(...)` combinators over
an `Observable.Start(..., TaskPoolScheduler.Default)` source
(`GetInstalledApplicationsObservable()`). If that source's factory delegate throws — e.g. the real
Windows shell acquisition fails — `Or()` silently swallows the error when the underlying source
has multiple subscribers (which it does here: `primaryAumidCache` is subscribed both directly via
`Or()` and via the `partialAumidCache`/`packagePathCache`/`targetPathCache` derived from it). No
`OnNext`, `OnError`, or `OnCompleted` reaches `Connect()` subscribers — the app list just stays
empty forever, with no signal to anyone.

This is **pre-existing behavior**, not introduced by the `shell-smtc-source-abstraction` branch —
it was only discovered because that branch tried to write a test asserting `OnError` propagation
for the new `IShellApplicationSource` seam and found it doesn't happen. See
`docs/superpowers/plans/2026-09-11-shell-smtc-source-abstraction.md` for that work, and
`WinTabber.Api.Media.Tests/ShellApplications/InstalledApplicationRepositoryTests.cs`'s
`ApplicationsByAumid_StaysEmpty_WhenAppsFolderAcquisitionFails` test for the executable proof.

**Out of scope to fix here.** Needs its own design pass if it's ever worth fixing — e.g. surfacing
failures via a separate error observable/property rather than through the cache, since the cache
itself has no natural way to represent "acquisition failed."
