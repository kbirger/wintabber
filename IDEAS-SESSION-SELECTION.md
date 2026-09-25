# Ideas: Media Session Selection Is Lost On A Track Skip

## Symptom

In the media controls window, skip to the next track. The skip works. The
selected playback session then clears. Open the dropdown and the session is
still listed. Both the WPF app and the WinUI 3 app show this.

## The design flaw

One property, `MediaControlsViewModel.SelectedSessionListItem`, has three
writers:

1. the Rx pipeline, when SMTC reports a new active session
2. the user, clicking the dropdown
3. the binding engine's bookkeeping, when the bound item leaves the collection

All three land in the same setter as a bare value. Provenance is destroyed at
the moment of the write. Line 175 then merges that property back into a
pipeline alongside `activeSessionChanges`, so nothing downstream can tell a
user's choice from a framework artifact either.

Every gate in this file exists to reconstruct that lost distinction, by
guessing:

- `DistinctUntilChanged(session => session?.Session.Key)` at line 151 guesses
  that the same key means "not user intent".
- `Throttle(TimeSpan.FromMilliseconds(250))` at line 178 guesses that changes
  closer together than 250 ms are noise.

The bug below is one case where a guess is wrong. It is not deep. The model
made it unobservable.

**A second, related flaw:** the selection is stored as a `SessionListItem`
object. That object is rebuilt whenever the session leaves and re-enters the
cache, so object identity cannot survive a track skip. An AUMID can.

## The failure, in detail

`AggregateSession.Key` is `(IsComplete, Aumid)`. `IsComplete` reports whether a
native audio session is matched to the SMTC session. It has nothing to do with
selection.

On a track skip, SMTC removes the session from its snapshot and adds it back
between 62 ms and 950 ms later. While it is gone:

1. The bound collection loses the item, so the ComboBox clears `SelectedItem`.
2. The two-way `SelectedItem` binding writes `null` into
   `SelectedSessionListItem`. The caller chain proves this is the binding
   engine, not the view model.
3. The session returns, and `WatchValue` emits it.
4. `DistinctUntilChanged` still holds the key from before the removal, so that
   emission looks like a duplicate. Nothing reassigns the selection.

**Recovery depends on an accident.** If the native audio session happens to
drop alongside the SMTC session, `IsComplete` flips, the key changes, the gate
opens, and the selection is restored. If the native session stays attached, the
key is unchanged and the selection stays cleared.

Two traces, same removal, opposite results:

```
Recovers (IsComplete flapped):
  22:02:28.453  [4] PASSED DistinctUntilChanged: key=(False, Brave)
  22:02:28.461  Remove Brave
  22:02:28.462  SET 'Brave' -> '<null>'
  22:02:29.184  Add Brave
  22:02:29.185  [4] PASSED DistinctUntilChanged: key=(True, Brave)
  22:02:29.187  SET '<null>' -> 'Brave'                        restored

Sticks (IsComplete stayed True):
  21:12:14.434  Remove nora
  21:12:14.439  SET 'nora' -> '<null>'
  21:12:14.507  Add nora
  21:12:14.508  [2] WatchValue emitted: key=(True, nora)
  21:12:14.508  [3] before DistinctUntilChanged: key=(True, nora)
                ... no [4]. Swallowed as a duplicate.
```

## Measurements

Gap between `Remove` and `Add` on a skip, measured from the bound collection:

| Client | Gap |
|---|---|
| Nora | 73 ms |
| Deezer in Brave | 62 ms |
| Deezer in Brave | 793 ms |
| Deezer in Brave | 718 ms |
| Deezer in Brave | 491 ms |
| Deezer in Brave | 950 ms |

The spread is fifteenfold. A genuine removal is also slow to detect: after the
Nora app closed, its session stayed listed for several seconds. No timer can
separate a skip from an ending.

## Four hypotheses that the evidence disproved

Recorded so nobody retries them.

1. **Duplicate `SessionListItem` instances.** `sessions` was a cold chain with
   `DisposeMany()` in it, subscribed by both `Bind` and every `WatchValue`
   under a `Switch`. Each subscription ran its own `Transform`, so
   `SelectedSessionListItem` held an equal-by-Aumid twin, and every
   `ActiveSession` emission disposed it. This was real and is fixed in commit
   `5141f61` with `AsObservableCache`. **It was not this bug.** The symptom
   survived the fix.
2. **The bound item is replaced.** There are no `Update` changesets at all. A
   trace of every changeset reason showed only `Add`, `Refresh` and `Remove`.
3. **Deactivation cycles.** Hiding and showing the window was assumed to tear
   down `WhenActivated`. Instrumented directly: **one** `ACTIVATED`, **zero**
   `DEACTIVATED`, across about twenty show and hide cycles. An earlier reading
   of repeated `Add` bursts as "three activations" was wrong.
4. **The churn is specific to one bad client.** Deezer in Brave removes and
   re-adds exactly as Nora does.

## Four patches, all rejected

Each of these infers what a write meant, after the information that would say
so was already discarded. They differ in quality, not in kind. Keep this list
so none of them is tried again.

**A. Debounce the snapshot stream upstream.** A `Throttle` before `EditDiff` in
`SMTCSessionRepository.ToSessionChangeSet` would hide a transient absence from
every consumer. Rejected on measurement: a window wide enough for 950 ms delays
every add and remove by about a second, and a closed app lingers even longer
than it already does. **A narrower variant did ship later** -- see "Where this
landed". Delaying only removals costs nothing on the add path, and once the
selection model was fixed the window no longer carried any correctness weight.

**B. Repair the selection in the view model.** Keep the gate; when the
selection goes null, wait for the session to return and put it back. Rejected
as compensation for state the view already corrupted, and it needs a mutable
`lastActive` field held across the activation.

**C. Reject a null written by the view.** Rejected: the selection would keep
pointing at the **old** `SessionListItem`, the `Add` builds a new instance, and
the ComboBox ends up with a `SelectedItem` that is not among its items, showing
blank permanently.

**D. Ask the cache instead of remembering a key.** A `.Where` testing whether
`SelectedSessionListItem` is still in `sessionCache`. Rejected for a concrete
defect: the gate sits at line 151, **before** the `ObserveOn` at line 153, so it
would read UI-thread state from the cache's thread. The trace confirms these
differ — `[4] PASSED` appears on threads 40, 44 and 11, while every
`SET SelectedSessionListItem` is on thread 2. It would read a stale value under
exactly the timing the bug needs.

## The model to build instead

Two inputs, one derived output, no gates.

- **The user's pick**, stored as an **AUMID string**, not an object, and written
  only by a real user action. A string survives the remove-and-add round trip
  that destroys object identity.
- **The SMTC-active session**, as it already arrives.
- **The effective selection**, derived: the user's pick when that AUMID is still
  in the cache, otherwise the SMTC-active session.

The ComboBox binds `SelectedItem` one way to the derived value. User choices
arrive on their own channel, where `null` is not representable.

What this buys, with no inference rule anywhere:

| Situation | Why it works |
|---|---|
| Track skip | The AUMID never changed. The derived value re-resolves to the new item. |
| User picks another session | The pick is held in its own state, not protected by a key comparison that might not fire. |
| Selected session really ends | The AUMID stops resolving in the cache. The derived value falls back on its own. |
| Binding engine writes null | It cannot. The binding is one way. |

The 62-to-950 ms spread stops mattering, because nothing is timing-dependent.

## Open questions for the implementation

- **How user picks arrive.** With `SelectedItem` one way, picks need a route.
  `SelectionChanged` also fires for programmatic changes, so using it naively
  reintroduces the same ambiguity through another door.
- **Whether `Throttle(250)` at line 178 is still load-bearing.** It is a second
  guess of the same kind, on the pipeline that rebuilds `ActiveSession.Session`.
  Nothing in the traces implicates it, but it should not be assumed free to
  keep once the selection model changes underneath it.

## Not this bug, but found on the way

- **Nora's session is not removed promptly when the app closes.** It stayed
  listed for several seconds.
- **The media window hotkey shows show-then-hide pairs 178 ms and 274 ms
  apart**, from the hotkey taskpool thread, not from the foreground watcher.
  A candidate cause for the window flashing and hiding. Not yet separated from
  two fast key presses.
- **An audible audio gap on the first window open only.** `creating device
  selectors` spans 4 ms and runs once, so the candidate is first-time WASAPI or
  COM initialisation, not a per-activation cost.

## How this was diagnosed

A temporary file tracer wrote to `%TEMP%\wintabber-trace.log`. `Debug.WriteLine`
is useless here, because the app runs detached with no debugger attached. The
probes that mattered:

- a stack trace in the `SelectedSessionListItem` setter, which proved the
  binding engine writes the null
- the `Reason` of every changeset reaching `Bind`, which ruled out `Update`
- `ACTIVATED` and `DEACTIVATED` markers, which ruled out the deactivation theory

Reproduce with the media window's **own** next button, not the player's. Any
click into the player moves the foreground, and `MediaControlsStateService`
hides the window, which stops the trace.

## Where this landed

Two commits, in this order. The split is deliberate: the first fixes the bug,
the second is cosmetic and can be reverted on its own.

**The selection model.** `MediaControlsViewModel` now takes two inputs -- the
user's pick as an AUMID, and the SMTC-active AUMID -- and derives the effective
selection against `sessionCache`. The public setter is the view's input channel
and drops nulls; `SetSelectionFromModel` is the model's output channel. Both
gates are gone. Verified live: six skips, six deterministic recoveries.

After this, the selection always returned to the right session, but the ComboBox
still went blank for the 233 ms to 950 ms the item was missing from the list.

**Delayed removals.** `ToSessionChangeSet` gained `removalDelay` (default 1.5 s)
and an `IScheduler`. Adds and updates apply at once; a removal is a proposal
that must survive the window. Built from per-key presence events, `GroupBy`, and
`Select`/`Delay`/`Switch` -- `Switch` cancels a pending removal when the key
returns, so there is no timer dictionary and no cancellation bookkeeping.
`EditDiff` is no longer used.

A test capturing changeset reasons showed a skip-shaped round trip now produces
exactly one changeset, `Add`, with no `Remove` and no `Update`. Confirmed live:
no blank, and the list itself stays stable. A genuinely closed app lingers up to
1.5 s longer, which is small beside the several seconds SMTC already takes.
