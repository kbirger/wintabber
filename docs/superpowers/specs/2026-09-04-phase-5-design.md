# Phase 5 Design — Cleanup Branch

Covers the four design-work tasks deferred from `.cleanup/tasks.md`'s Phase 5 (T5.1–T5.4).
Phases 0–4 are complete on the `cleanup` branch (last commit `e3b85af`). This spec is the
"plan separately" pass `tasks.md` calls for; `.cleanup/HANDOFF.md` and `tasks.md` remain the
source of record for what already happened in Phases 0–4.

Sequencing: **T5.1 → T5.2** (T5.2's fix depends on `IProcessControl` existing). T5.3 and T5.4
are independent of those two and of each other.

---

## T5.1 — Split `IInteropProxy`

### Problem

`IInteropProxy` (`WinTabber.Interop/IInteropProxy.cs`) has 39 members covering six unrelated
concerns. `WinTabber.Api.Windowing.Tests/Fakes/FakeInteropProxy.cs` — the only fake in the
repo — must implement all 39 to satisfy the interface, even though the Suspension tests it
serves exercise only 8 of them; the other 31 throw `NotSupportedException` so accidental use
fails loudly. `InteropProxy` (the concrete class) is untestable P/Invoke glue and stays that
way regardless of this split — the split is about consumers, not about testing `InteropProxy`
itself.

### Call-site mapping (verified against the current tree, not the task list's guess)

Every member was traced to its actual callers via `Grep`/Serena before grouping:

| Member | Consumers |
|---|---|
| `SuspendProcess`, `ResumeProcess` | `NtProcessSuspensionStrategy` |
| `SuspendProcessThreads`, `ResumeProcessThreads` | `ThreadSuspensionStrategy` |
| `GetProcessImagePath` | `ProcessSuspensionService` |
| `EnableDebugPrivilege` | `BackgroundServiceContainer` (see T5.2) |
| `HideWindow`, `RestoreWindow` | `ProcessSuspensionService` |
| `MoveWindowOffScreen`, `RestoreWindowPosition`, `ResizeWindow`, `HideFromTaskbar`, `RestoreExtendedStyle` | `WindowThumbnailService` only |
| `GetForegroundWindowHandle`, `GetWindowTitle`, `GetClassName`, `IsWindowVisible`, `IsTopLevel`, `GetWindowStyles`, `GetWindowState`, `GetWindowPlacement`, `IsWindow` | `WindowRef`, `WindowOwner`, `ApplicationRef`, coordinators, `WindowThumbnailService`, Events |
| `BringWindowToFront`, `MaximizeWindow`, `MinimizeWindow`, `CloseWindow`, `MoveWindow`, `SetWindowText`, `ActivateLivePreview`, `DeactivateLivePreview` | `WindowRef`, `WindowManager` |
| `ForceForeground` | `MediaWindowViewCoordinator` |
| `MakeWindowNonActivating` | `SuspendedWindowsWindow.xaml.cs`, `MediaDebugWindow.xaml.cs` (via `Ioc.Default`, a pre-existing T6.2 item, unaffected by this split) |
| `EnumerateProcessWindowHandles`, `GetWindowProcess`, `GetWindowProcessId`, `GetForegroundProcess`, `IsProcessElevated` | `WindowManager`, `WindowProcessRef`, `WindowOwner`, `WindowThumbnailService`, `MediaControlsStateService`, Events |
| `SendInput` | `HyperKeyState` (Events) |
| `ActiveWindowChangedEvents` | `WinTabberEventManager` (Events) |

### Design

Four interfaces, all implemented by the unchanged `InteropProxy`:

- **`IProcessControl`** (6 members) — `SuspendProcess`, `ResumeProcess`, `SuspendProcessThreads`,
  `ResumeProcessThreads`, `GetProcessImagePath`, `EnableDebugPrivilege`. Consumed by
  `Suspension/*` and `BackgroundServiceContainer`'s `EnableDebugPrivilege` call (T5.2 moves that
  call; see below).
- **`IWindowVisibility`** (2 members) — `HideWindow`, `RestoreWindow`. These look
  process-suspension-flavored (their only current caller is `ProcessSuspensionService`), but they
  are generic window-visibility operations, not process operations, so they don't belong in
  `IProcessControl` on the strength of a single caller. Split out as their own tiny interface
  rather than folded into the catch-all so `ProcessSuspensionService` can depend on exactly what
  it uses (see fake-sizing note below).
- **`IWindowPlacement`** (5 members) — `MoveWindowOffScreen`, `RestoreWindowPosition`,
  `ResizeWindow`, `HideFromTaskbar`, `RestoreExtendedStyle`. Consumed only by
  `WindowThumbnailService`.
- **`IWindowInterop : IWindowVisibility`** (26 own members + the 2 inherited = 28 total) —
  everything else. Inherits `IWindowVisibility` so every existing consumer that needs
  `HideWindow`/`RestoreWindow` conceptually as part of "window interop" still gets them from
  `IWindowInterop` with no source change; the inheritance only matters to a consumer that wants
  the narrower slice.

`InteropProxy` implements `IProcessControl`, `IWindowPlacement`, and `IWindowInterop` (which
brings `IWindowVisibility` along transitively); nothing about its internals changes. Consumers
narrow their constructor dependency to whichever interface(s) they actually call.

**Effect on `FakeInteropProxy`**: `ProcessSuspensionService` depends on `IProcessControl` +
`IWindowVisibility` (8 members total, not the full `IWindowInterop`) — a single fake class
implementing both, all 8 members real, zero throw-stubs. `NtProcessSuspensionStrategy`/
`ThreadSuspensionStrategy` depend on `IProcessControl` alone. Down from one 39-member fake (31
throwing) to one 8-member fake (0 throwing). Without the `IWindowVisibility` split, the fake
would still need to stub all 28 `IWindowInterop` members (26 throwing) alongside
`IProcessControl` — barely smaller than today — which is why the split exists.

### T3.4 carryover — corrected: not a duplicate, one dead + one live

T3.4 described `WinTabber.Infrastructure/AppCache.cs:129` and
`WinTabber.Api.Media/.../InstalledApplicationRepository.cs:168` as a verbatim-duplicate
`DeleteObject` + `Bitmap2BitmapImage`. Re-checked while drafting the implementation plan: that's
wrong. `InstalledApplicationRepository.Bitmap2BitmapImage` (private, static) has exactly two
call sites in the file, both inside comments (`InstalledApplicationRepository.cs:198,294` —
`//var zz = Bitmap2BitmapImage(bitmap);` / `//var image = Bitmap2BitmapImage(bitmap);`). It is
dead code, same class of finding as Phase 1's T1.3/T1.8, not a live duplicate of
`AppCache.Bitmap2BitmapImage`. There is nothing to deduplicate — only `AppCache.cs`'s copy runs.

**Fix**: delete `InstalledApplicationRepository.cs`'s `Bitmap2BitmapImage` method, its
`[DllImport("gdi32.dll")] DeleteObject`, and the two dead comment lines referencing it. Migrate
`AppCache.cs`'s `DeleteObject` in place to CsWin32 (`PInvoke.DeleteObject` from
`Windows.Win32.Graphics.Gdi`, matching how `InstalledApplicationRepository.cs` already consumes
CsWin32 in the same project family) — `WinTabber.Infrastructure` currently has no
`NativeMethods.txt` and no `Microsoft.Windows.CsWin32` package reference, so both are added
(with `<PrivateAssets>all</PrivateAssets>`, the omission T1.6 flagged elsewhere). No shared
helper, no new project reference, no `WinTabber.Common.Util` involvement — a single live call
site doesn't justify one (YAGNI).

### DllImport inventory — dedup and CsWin32 migration audit

The repo has 8 hand-written `DllImport` declarations across 6 files (verified via `Grep`, not
carried over from the Phase 3 count, which was scoped to policy compliance rather than to
CsWin32 availability). T3.4 signed off on 3 of these locations as "fine under either policy"
without checking whether CsWin32 metadata now covers the specific functions — that check is
folded into T5.1 since it's the same interop-cleanup scope:

| File | API(s) | CsWin32 metadata available? | Action |
|---|---|---|---|
| `WinTabber.Interop/NtNativeMethods.cs` | `NtSuspendProcess`, `NtResumeProcess` (`ntdll.dll`) | No — undocumented NT exports, file's own doc comment confirms this | Keep hand-written, already in `WinTabber.Interop` |
| `WinTabber.Interop/PInvoke.cs:43` | `DwmpActivateLivePreview` (`dwmapi.dll`, ordinal `#113`) | No — unnamed ordinal export, no public documentation | Keep hand-written, already in `WinTabber.Interop` |
| `WinTabber.UI.Common/Chrome/Interop.cs:7` | `SetWindowCompositionAttribute` (`user32.dll`) | No — undocumented (confirmed in T3.4) | Keep hand-written, but **relocate** to `WinTabber.Interop` — see below |
| `WinTabber.Interop/UacHelper.cs:17,21` | `OpenProcessToken`, `GetTokenInformation` (`advapi32.dll`) | **Yes** — both are standard, documented Win32 APIs | **Migrate to CsWin32** — add both to `WinTabber.Interop/NativeMethods.txt`, switch to `PInvoke.OpenProcessToken`/`PInvoke.GetTokenInformation`. Note CsWin32's generated signatures use safe handles and the `Windows.Win32.Security.TOKEN_INFORMATION_CLASS`/`TOKEN_ELEVATION_TYPE` types rather than the hand-rolled `IntPtr`s and the private `TOKEN_INFORMATION_CLASS` enum this file currently declares — expect signature adaptation, not a drop-in rename. Verify with a build, per CLAUDE.md's "the build is the arbiter." `UacHelper.IsProcessElevated(int)` / `IsProcessElevated(Process)` are also near-verbatim duplicates of each other (T5.4 can cover the pure parts of this with a unit test once elevation-type interpretation is isolated from the token P/Invoke calls, but that split is not required for the CsWin32 migration itself). |
| `WinTabber.Infrastructure/AppCache.cs:129` | `DeleteObject` (`gdi32.dll`) | **Yes** | Migrate in place to `PInvoke.DeleteObject` — see T3.4 correction above. |
| `WinTabber.Api.Media/.../InstalledApplicationRepository.cs:168` | `DeleteObject` (`gdi32.dll`) | **Yes** | **Dead code** (see T3.4 correction) — delete the method and the `DllImport` entirely, no migration needed. |

Net: 8 hand-written `DllImport`s → 3 (all confirmed to have no CsWin32 metadata) plus 1 deleted
as dead code, plus one new CsWin32 `NativeMethods.txt` entry in a project (`WinTabber.Infrastructure`)
that currently has none.

**Policy correction: undocumented hand-written imports consolidate into `WinTabber.Interop`,
even ones that affect our own window's rendering.** T3.1/CLAUDE.md's chrome carve-out
("Win32 that affects the rendering of our own windows... lives with the WPF code that owns the
`HwndSource`") was written with CsWin32-backed chrome APIs in mind (`DwmSetWindowAttribute`,
`DWM_WINDOW_CORNER_PREFERENCE` — both still correctly live in
`WinTabber.UI.Common/NativeMethods.txt` per T3.6, unaffected by this). It does not hold for a
*hand-written, undocumented* import: there is no seam/testability argument against moving those,
and scattering hand-rolled `DllImport`s across projects by what-they-act-on is worse than having
exactly one place a reader checks for "undocumented Win32 we depend on." `SetWindowCompositionAttribute`
therefore moves into `WinTabber.Interop` (e.g. alongside `NtNativeMethods.cs`, or its own file) as
an internal static wrapper; `WindowCompositionAttributeData` and the `WindowCompositionAttribute`
enum move with it since the signature depends on them. `WinTabber.UI.Common/Chrome/Interop.cs`'s
higher-level `EnableBlur`/`SetAccentPolicy` (the `AccentPolicy`/`AccentState`/`AccentFlags`
marshaling, which is chrome-specific, not a raw import) stays in `UI.Common/Chrome` and calls the
relocated wrapper — this adds a new `WinTabber.UI.Common → WinTabber.Interop` project reference,
which does not exist today.

This narrows CLAUDE.md's **Windows Interop** rule: the own-window-rendering carve-out applies to
CsWin32-backed chrome Win32 (kept local, each project's own `NativeMethods.txt`); *hand-written*
declarations for undocumented APIs always live in `WinTabber.Interop`, regardless of what they
act on. Update CLAUDE.md's Windows Interop section to state this narrower rule as part of
implementing this task, the same way T3.5 updated it for the original policy.

### T3.1 revisit

No policy change. Splitting `IInteropProxy` removes T3.1's "(a) would grow an already-overloaded
39-member interface" observation, but T3.1's decisive argument survives unchanged: an interface
belongs where its consumers are, and the chrome call sites (`CloakHelper`, `PeekHelper`,
`CornerHelper`) are untestable WPF code living in `WinTabber.UI.Common`/`WinTabberUI`, not
`WinTabber.Interop`. State this conclusion in the implementation; do not reopen policy (a) vs
(b).

---

## T5.2 — Make `BackgroundServiceContainer` ordering explicit

### Problem

`BackgroundServiceContainer`'s constructor (`WinTabberUI/BackgroundServiceContainer.cs:19-63`)
has two ordering requirements enforced only by comments and by position in a
`CompositeDisposable(...)` argument list:

1. `EnableDebugPrivilege()` must run before any suspend attempt.
2. `MediaDebugWindowCoordinator.Init()` must run after `MediaWindowViewCoordinator.Init()`.

`ViewCoordinatorBase.Init()` (`WinTabberUI/Coordinators/ViewCoordinatorBase.cs:39-43`) subscribes
to `GetChangeEvents()` via `.ObserveOnDispatcher()`. Both `MediaWindowViewCoordinator` and
`MediaDebugWindowCoordinator` independently derive their trigger from the same underlying
`ApplicationStateViewModel.IsMediaControlsActiveChanges` subject (the debug coordinator combines
it with its own enabled-toggle). Correctness today depends on dispatcher-queue ordering, which
in turn depends on *subscribe order*, which in turn depends on where each
`ioc.GetRequiredService<...>().Init()` call sits in the composite's argument list. Reordering two
lines compiles and builds clean, and breaks this at runtime.

### Design

- **Debug-window-after-media-window**: add a `ShownChanges` observable to
  `ViewCoordinatorBase<T>` that reflects the coordinator's actual post-`Show()` state (not the
  raw upstream trigger). Give `MediaDebugWindowCoordinator` a constructor dependency on
  `MediaWindowViewCoordinator` and have `GetChangeEvents()` combine
  `mediaWindowCoordinator.ShownChanges` with `_debugState.IsEnabledChanges`, replacing the
  independent read of `_vm.IsMediaControlsActiveChanges`. The debug coordinator now reacts to the
  media coordinator's actual effect, not to a second, independently-timed subscription to the
  same source — so its position in `BackgroundServiceContainer`'s list stops mattering.
- **`EnableDebugPrivilege` before any suspend**: move the call out of
  `BackgroundServiceContainer` and into `ProcessSuspensionService`'s own constructor (it already
  takes an interop dependency; after T5.1 it takes `IProcessControl`, which owns
  `EnableDebugPrivilege`). The class with the precondition establishes it itself in its own
  constructor body — there is no longer an ordering requirement for `BackgroundServiceContainer`
  to get right.

The explanatory comments in both files stay — reworded where needed to describe the new
explicit mechanism (e.g. "no longer order-dependent: `MediaDebugWindowCoordinator` observes
`MediaWindowViewCoordinator` directly") rather than removed.

---

## T5.3 — Test project for `WinTabber.Api.Media`

### Design

New `WinTabber.Api.Media.Tests` project (TUnit, mirroring `WinTabber.Api.Windowing.Tests`'s
shape: a `Fakes/` folder, direct project reference to `WinTabber.Api.Media`).

**In scope**: `CoreAudioDeviceRepository`/`CoreAudioSessionRepository` business logic, tested
against the *existing* `IMMDeviceEnumeratorWrapper` seam
(`WinTabber.Api.Media/CoreAudio/IMMDeviceEnumeratorWrapper.cs`) via a new
`FakeMMDeviceEnumeratorWrapper`. This seam already exists and is currently exercised by nothing.

**Explicitly out of scope, and why** (mirrors the T3.1 testability-tracks-the-seam argument):

- `PolicyConfigClient` — raw `IPolicyConfig` COM interop, no seam, would need a real audio
  endpoint to exercise meaningfully.
- `STAScheduler.Create()` — creates a real STA thread; not unit-testable, and not worth wrapping
  behind an interface only to verify "creates a thread." Consumers that need scheduling should
  accept an `IScheduler` via constructor so tests can substitute `ImmediateScheduler`/
  `TestScheduler`; check each consumer for this at implementation time rather than introducing a
  wrapper speculatively.
- SMTC (`SMTCSessionRepository`/`SMTCSessionMonitor`/`SMTCSessionService`) and ShellApplications
  (`InstalledApplicationRepository`)'s WinRT/COM glue — no existing seam. Do not manufacture one
  without a concrete test driving the need (YAGNI); if a future task wants coverage here, that is
  a new, separately-scoped design decision, not part of this one.

Document the excluded surfaces inline in the test project (a short `README.md` or top-of-file
comment) the same way `docs/testability-future-work.md` documents deferred items, so "zero
tests" reads as a decision, not an oversight.

---

## T5.4 — Test coverage for `WinTabber.Interop`

### Design

Read every source file in `WinTabber.Interop/`. Findings:

- `WindowPlacement.cs`, `ProcessInfo.cs` — plain data records/DTOs. Nothing to test.
- `InteropProxy.cs`, `NativeMethods.cs`, `NtNativeMethods.cs`, `PInvoke.cs`, `MediaKeySender.cs`,
  `UacHelper.cs`'s elevation check — P/Invoke wrapper glue. Same argument T3.1 already made about
  `CloakHelper`/`PeekHelper`: a test here would only verify that the wrapper calls the Win32
  function it wraps (mock-verifies-the-mock), not that the behavior is correct.
- `ProcessHelper.IsSystemProcess(Process)` (`WinTabber.Interop/ProcessHelper.cs:23`) — pure
  logic, already directly testable with `Process.GetCurrentProcess()`, no faking needed.
- `ProcessHelper.GetNonSystemProcesses()` (`ProcessHelper.cs:133`) — the parent/child
  system-process propagation walk (`isSelfSystem` / `isParentSystem` / `processMap`) is pure
  logic, but it's currently fused to the live `CreateToolhelp32Snapshot` enumeration via
  `GetProcesses()`.

**Design**: extract the classification walk out of `GetNonSystemProcesses()` into a pure function
taking `IEnumerable<ProcessInfo>` (e.g. `ClassifyNonSystemProcesses(IEnumerable<ProcessInfo>)`),
with `GetNonSystemProcesses()` becoming `ClassifyNonSystemProcesses(GetProcesses())`. New
`WinTabber.Interop.Tests` project covers `IsSystemProcess` and the extracted classification
function with synthetic `ProcessInfo` trees (system parent propagating to children, `svchost`
special-case, root/id-0 handling). The P/Invoke surface stays untested by design, documented the
same way as T5.3's exclusions.

---

## Open items carried forward (not part of this spec)

- `SuspendedWindowsWindow.xaml.cs` / `MediaDebugWindow.xaml.cs` resolving `IInteropProxy` via
  `Ioc.Default` instead of constructor injection — pre-existing, tracked as T6.2. Unaffected by
  the T5.1 split (both call `MakeWindowNonActivating`, which lands in `IWindowInterop`).
