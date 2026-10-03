# Ideas: Icon Cache Expiration

## Problem

`InstalledApplicationRepository` loads each app's icon as a `Bitmap` through an
observable built like this (in `GetIcon` and `GetCachedIcon`):

```csharp
Observable.Defer(() => Observable.Start(...)).Replay(1).AutoConnect()
```

`Defer` makes the load lazy: an icon nobody has ever viewed costs nothing. But
`Replay(1).AutoConnect()` never disconnects once the first subscriber
appears, so a `Bitmap` stays cached in memory forever after that, whether or
not anyone is still displaying it.

This matters because `System.Drawing.Bitmap` wraps an unmanaged GDI+ handle.
The code today never calls `Dispose()` on these `Bitmap` objects — cleanup
relies entirely on the GC finalizer, which runs at an unpredictable time.
Nothing currently expires an unused icon or disposes it.

## Design principle that applies to every option below

Whatever mechanism decides when to expire an icon, the clock must start when
the **last Rx subscriber actually disconnects** — not merely "time since the
cache was last touched." A plain time-since-touch scheme (sliding
expiration, or DynamicData's `ExpireAfter` used naively) can dispose a
`Bitmap` that is still being displayed, if the UI holds one continuous
subscription for a long stretch without ever resubscribing to refresh the
timer. This is a real use-after-dispose risk, not a hypothetical one.

The fix, common to every option: don't hand the value to the expiring cache
at load time. Keep it as a plain in-flight value while at least one
subscriber is connected — immune to expiry. Only insert it into the
expiring cache at the moment the connection actually drops to zero
subscribers. A resubscribe within the expiry window pulls it back out
before it can be disposed.

## Option A — Custom Rx operator (hand-rolled timer)

A small reusable operator (e.g. `.ExpiringReplay(expiryDelay, disposeAction, scheduler)`)
that:
- Tracks its own subscriber count (not Rx's built-in `RefCount`).
- Holds the cached value in a slot it fully controls (not a `ReplaySubject`'s
  buffer), so it can discard the old value and swap in a fresh one — never
  handing a disposed `Bitmap` to a new subscriber.
- On the last unsubscribe, schedules a one-shot timer via `IScheduler`.
- A new subscriber before the timer fires cancels it and reuses the cached
  value (no reload, no dispose).
- If the timer fires with still no subscribers, calls the dispose action on
  the cached value, discards it, and resets state so the next subscription
  loads fresh.

**Verified, not assumed:** the obvious shortcut — `source.Replay(1).RefCount(TimeSpan)`,
a real overload in `System.Reactive` 6.1.0 — does **not** work for this.
Tested it directly: after the disconnect-delay elapses and a new subscriber
arrives, Rx hands them the stale cached value first (from the `ReplaySubject`
buffer, which is never cleared on disconnect), and only then triggers a
fresh reload. It cannot be used to safely dispose the old value at
expiration.

**Trade-off:** entirely self-contained (no new dependency), but it's real
code to write and test — subscriber counting, timer scheduling, and
correct handling of races between "timer about to fire" and "new subscriber
arriving" all need to be right. Would need its own unit tests using Rx's
`TestScheduler` (virtual time).

## Option B — `IMemoryCache`/`MemoryCache` + `PostEvictionCallbackRegistration`

Use a memory cache with a sliding or absolute expiration and a
`PostEvictionCallback` that calls `Dispose()` on the evicted `Bitmap`. An
observable checks the cache first; on a miss, loads fresh and inserts.

Applying the "insert only at last-unsubscribe" principle above: don't insert
at load time. Insert into the cache (starting its expiration countdown)
only when the underlying Rx connection actually disconnects. A resubscribe
before expiry pulls the entry back out (removing it from the cache) and
reuses the live object.

**Trade-off:** `System.Runtime.Caching` is already a package reference
elsewhere in the solution (it was on `WinTabber.Infrastructure`'s now-deleted
`AppCache`, and remains for `WinTabber.Api.Windowing`), so it's not a brand
new dependency, but this class doesn't otherwise use anything like a
memory-cache abstraction — it would be a new pattern *for this file*, less
consistent with what `InstalledApplicationRepository` already does
throughout (heavy DynamicData usage).

## Option C — Model it as a DynamicData cache (recommended)

`InstalledApplicationRepository` already depends on DynamicData and already
builds a `SourceCache<InstalledApplicationInfo, string>` (`_apps`) for the
app list itself. This option reuses the same toolkit for icons: a
`SourceCache<CachedIcon, string>` keyed by AUMID, wired once for the
repository's lifetime:

```csharp
iconCache.Connect()
    .OnItemRemoved(icon => icon.Bitmap.Dispose())
    .ExpireAfter(icon => idleWindow)
    .Subscribe();
```

**Verified empirically against the pinned DynamicData 9.4.1, not assumed:**
- `OnItemRemoved`'s callback fires for a removal driven by `ExpireAfter`,
  not only for an explicit `.Remove()` call.
- Re-`AddOrUpdate`-ing the same key resets that item's `ExpireAfter` clock —
  confirmed by timing the actual disposal against a manual "touch" partway
  through the expiry window.

Applying the same "insert only at last-unsubscribe" principle: each app's
`Icon` observable becomes a `Defer` that first checks `iconCache.Lookup(key)` —
if found, `AddOrUpdate`s the same value again (canceling any pending
eviction) and returns it directly, no reload; if not found, loads fresh.
The `Defer` is wrapped in a plain `.Replay(1).RefCount()` (no `TimeSpan`
needed on `RefCount` itself — DynamicData now owns the actual retention
duration). The cache entry itself is only written at the point a
connection's subscriber count drops to zero, per the shared principle above.

**Trade-off:** fits this file's existing idiom better than introducing a
new caching abstraction, and the two operators needed (`OnItemRemoved`,
`ExpireAfter`) are both already verified to behave correctly for this use.
Still real code to write (the "check cache, touch, or load" `Defer` logic,
and the "insert only at zero-subscriber transition" wiring) and worth
covering with tests, though DynamicData's own operators remove the need to
hand-roll the timer/dedup logic Option A would require.

## Where this landed

Not yet decided which to build. Leaning toward Option C given it reuses an
already-proven, already-depended-on toolkit in this exact class, but no
implementation has started — this file is a record of the options explored
and what was verified about each, for whenever this gets picked back up.

## Outcome (2026-09-24): all three options rejected on measurement

None of A, B or C was built. Two findings closed the question.

**The trigger event never fired.** Every option expires an icon when the last
Rx subscriber disconnects. That never happened: `SessionListItem` never
disposed its `ObservableAsPropertyHelper`, and `MediaControlsViewModel` used
`.Transform` with no `DisposeMany()`. The `ReplaySubject` therefore held a
strong reference to every `SessionListItem` ever created, which leaked the view
models as well as the bitmaps.

**The bulk retention was not the UI path.** `TryPersistCache` called
`TryEncodeIcon(app.Icon)` for the whole catalog on a cold launch, and
`Replay(1).AutoConnect()` latched every bitmap for the life of the process.

Measured with a console probe against the real repository and an empty cache
directory: 389 apps-folder items, 349 icons, and every icon exactly 256x256 at
256 KB, for **87.2 MB** of pixel data. Both `MediaControlsWindow.xaml` files
draw that image at 16x16.

Fixes that landed instead (commits `a5e3b6b`, `f1006f7`, `82825f9`):

- `SessionListItem : IDisposable` plus `.DisposeMany()`, in both the WPF and the
  winui3 copy.
- A cold `InstalledApplicationInfo.IconSource` that the persist path subscribes
  to, so taking one value no longer latches `Icon`. Later made `internal`.

What remains after those fixes is the UI path alone: one 256 KB bitmap per live
media session, so at most about 1 MB. An expiry cache would reclaim that much,
in exchange for a hand-rolled Rx operator or a new caching abstraction in a
class already dense with subtle Rx behaviour. Not worth it.

**The better follow-up** is to request a smaller thumbnail size in `GetIcon`.
At 16x display size a 32x32 request is 4 KB instead of 256 KB, and it cuts
extraction cost, on-disk PNG cache size and UI retention together. Trap:
`TryPersistCache` reuses previous icon bytes whenever Name, TargetPath and
PackageInstallPath are unchanged. Size is not part of that comparison and
`CachedApplicationEntry` has no version field, so existing users keep their
256x256 PNGs until something invalidates the cache. A size change must ship
with an invalidation.
