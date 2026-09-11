# Handover note — 2026-09-11 (delete this file after reading)

## Where things stand

**`master` is at `e1d6fb1`, pushed, tagged `v0.2.2`.** Two branches landed and were deleted
(locally and on `origin`) this session:

- `shell-smtc-source-abstraction` (left push-only, unmerged by the previous handover) — merged,
  tagged `v0.2.1`.
- `docs-and-error-handling-fixes` (new this session) — merged, tagged `v0.2.2`.

Build is warning-free; 114/114 tests pass on `master` as merged. No plan file or SDD ledger for
this session's work beyond `v0.2.1`'s — the `v0.2.2` work (a doc fix and a bug fix) was small
enough to do directly, not through `subagent-driven-development`.

## What happened this session

1. **Merged `shell-smtc-source-abstraction` into `master`, tagged `v0.2.1`.** This branch had
   sat pushed-but-unmerged since the previous session (see `git log f4d169a..7ca4695` for what
   was in it — `ISmtcSessionSource`/`IShellApplicationSource`, closing items 9/10 of
   `.todos/testability-review.md`). Fast-forward merge, tests verified green before and after,
   both branch copies deleted.

2. **Fixed the `dotnet test --filter` doc bug the previous handover flagged but didn't fix.**
   `CLAUDE.md` and `docs/superpowers/plans/2026-09-11-shell-smtc-source-abstraction.md` (4 spots)
   documented `--filter`, which silently does nothing on this repo's Microsoft.Testing.Platform
   runner. Corrected to `dotnet test <project> -- --treenode-filter "/*/*/<ClassName>/*"` and
   verified the corrected form actually filters (ran 4/4 `TrieNodeTests`, not the full 114).

3. **Fixed the DynamicData `Or()` error-swallowing gap tracked in
   `.todos/installed-application-repository-error-handling.md`** (now deleted, resolved). This
   is the one worth reading closely if anything like it comes up again — see below.

4. **Merged `docs-and-error-handling-fixes` into `master`, tagged `v0.2.2`.**

## The DynamicData fix — what actually happened, since the original diagnosis was wrong

The tracked gap's own description (written by the previous session) theorized that `Or()`
swallows `OnError` because `primaryAumidCache` has multiple independent subscribers, each
re-running the cold `TaskPoolScheduler`-scheduled source. **That theory was tested and falsified
this session.** A reduced repro (`shared = FailingSource().Publish().RefCount()`, then
`shared.Or(shared.Filter(...))`) still swallowed the error even with exactly one shared execution
underneath both `Or()` branches. A single-subscriber baseline with no `Or()` at all propagated
correctly. So: **`Or()` swallows `OnError` unconditionally, not as an artifact of multi-subscription.**
If another DynamicData combinator (`And()`, `Except()`, `Merge()`, etc.) over an async source
seems to be dropping errors, don't assume sharing the source fixes it — verify with a reduced
repro first, the same way this session did.

**The fix:** stop routing failures through `Or()` at all. `InstalledApplicationRepository`'s
`GetInstalledApplicationsObservable()` is now wrapped in `.Catch<..., Exception>(...)` *before*
`.ToObservableChangeSet()` — a failure is reported on a new `AcquisitionErrors` observable
(added to `IInstalledApplicationRepository`) and the changeset completes as an empty list, so
`Or()`/`AutoRefreshOnObservable` never see an error to swallow. `ApplicationsByAumid`/
`ApplicationsByPath` keep their pre-existing "stays empty on failure" behavior; the difference is
that a failure is no longer *silent* — see `InstalledApplicationRepositoryTests
.AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails`.

**`AcquisitionErrors` is a `ReplaySubject<Exception>(1)`, not a plain `Subject`, and this cost a
debugging cycle to discover:** `AsObservableCache()` subscribes eagerly inside the repository's
constructor, so the background acquisition can fail and call `OnNext` on the error subject
*before* a test (or any real consumer) gets a chance to subscribe. With a plain `Subject` the
test timed out — the notification had already fired and was dropped on the floor. Same class of
"eager subscription races a late subscriber" gotcha as the `Or()`-multi-subscription theory
turned out not to be, just in a different spot. If a similarly-shaped "add an error/status
channel next to an eagerly-subscribed cache" fix comes up again, reach for `ReplaySubject(1)`
by default rather than `Subject`, and don't assume a passing local test proves it — this one
looked identical to the fixed version until the replay change, just slower to fail.

`.Publish().RefCount()` was also added to `primaryAumidCache` in the same change. It does **not**
fix the error-propagation bug (see above) — it's there because without it, the cache's several
downstream subscribers (the `Or()` branches, `AutoRefreshOnObservable`, and the three derived
partial/package/target caches) each independently re-run the real shell-acquisition call. Keeping
it means acquisition — and now `AcquisitionErrors.OnNext` — fires once per actual failure, not
once per subscriber.

## What didn't happen — still genuinely open

**The manual smoke tests are still unrun, now five items across five handovers:**
1. Window blur/chrome (`SetWindowCompositionAttribute`).
2. Media/debug window ordering with the tray toggle on.
3. Default audio device switching + volume/mute.
4. T6.6's media-controls activation fix (show/hide cycling).
5. **Icon rendering in the window selector / app list** — flagged two handovers ago after the
   `IShellApplicationSource` refactor, and now doubly relevant: this session changed the same
   repository's reactive composition again (`Catch()`, `Publish().RefCount()`). Nobody has
   confirmed the happy path (icons still load, app list still populates) against the real Windows
   shell since either change landed — only the fake-backed tests have run. See the previous
   assistant turn in this session's transcript for the exact manual-verification steps offered
   (temporarily force `WindowsShellApplicationSource.GetAppsFolder()` to throw, subscribe to
   `AcquisitionErrors` with a `Debug.WriteLine`, run under a debugger) if you want to exercise the
   failure path specifically; the user declined to have that done this session.

**`AcquisitionErrors` has no consumer.** It exists on the interface and fires correctly (proven
by test), but nothing in `WinTabberUI` subscribes to it yet — a shell-acquisition failure is now
*observable* but still invisible to an actual user. Wiring it into `MediaDebugViewModel` (or
somewhere a status message could show) is a reasonable next step if it's ever worth doing;
deliberately not done this session since no consumer was requested and real shell acquisition
essentially never throws.

**`.todos/window-selector-cleanup.md` item 2, the `Screen.Bounds`/`WorkingArea` half of item 3,
and item 4** — untouched, still open, same as every prior handover.

**The CI-tolerates-`Window.Show()` unknown is still unknown.** Untouched.

## How to work here

Everything from prior handovers about TDD specifics, the two-tier WPF testing split,
`[Lazy]`-generated members, `DisposeWith`'s namespace, `RxSchedulers` vs `RxApp`, "verify, don't
assert", and the interop-seam split still applies. `CLAUDE.md`'s test-filter command is now
correct — no need to rediscover the `--treenode-filter` syntax again.

`.cleanup/` is tracked, so anything added here lands in the repo. Delete this file once read.
