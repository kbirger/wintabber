# WinTabber Cleanup — Task List

Derived from [`architecture-review.md`](./architecture-review.md).
Baseline: `dev` @ `af16e91` — `dotnet build WinTabber.slnx` → 0 warnings, 0 errors.

**Rule for every phase:** finish with a clean build *and* a green test run.

```bash
dotnet build WinTabber.slnx          # must stay at 0 warnings
dotnet test --solution WinTabber.slnx
```

---

## Phase 0 — Prep

- [x] **T0.1** Decide whether `.cleanup/` is committed or ignored. Resolved: `.cleanup/` is
      already tracked and committed (`git status` is clean on it) — keep it committed.
- [x] **T0.2** Branch off `dev` for the cleanup work. Created branch `cleanup` off `dev`.
- [x] **T0.3** Record the baseline test count before deleting anything, so T1.5's drop is
      explainable: `dotnet test --solution WinTabber.slnx` → **133 passed, 0 failed, 0 skipped**
      (build: 0 warnings, 0 errors).

---

## Phase 1 — Pure deletion

No design decisions. One commit, or one per task. Removes ~1,700 lines.
**Do T1.3 before Phase 3** — it removes 8 of the 11 stray `DllImport`s on its own.

- [x] **T1.1** Delete dead project `WinTabber/` (`Program.cs`, `Main.cs` — both 100% commented
      out, 88 LOC). Not in `WinTabber.slnx`. *(F4)*
- [x] **T1.2** Delete dead project `WinTabber.GameBar/` — `.csproj` only, zero source files,
      targets net9.0, pins `CsWinRT 2.2.0` / `System.Reactive 6.1.0` inline against
      `Directory.Packages.props`. *(F4)*
- [x] **T1.3** Delete orphan types — none referenced outside their own file: *(F6)*
      - [x] `WinTabberUI/WindowHelper2.cs` (289 LOC, **8 `DllImport`s**)
      - [x] `WinTabberUI/Infrastructure/AumidHelpers.cs` (223 LOC — all four static methods
            uncalled) ⟵ *not in the original F6 list; found during task breakdown*
      - [x] `WinTabber.UI.Media/ViewModels/MediaSessionVm.cs` (123 LOC, 82% commented)
      - [x] `WinTabber.UI.Common/Behaviors/ControlledWindowBehavior.cs` (77 LOC, 81% commented)
            ⟵ *not in the original F6 list; found during task breakdown*
      - [x] `WinTabber.UI.Media/ViewModels/SelectionList.cs` + `ISelectable.cs` (48 LOC)
      - [x] `WinTabberUI/Interop/StaThreadHost.cs` (46 LOC)
      - [x] `WinTabberUI/Infrastructure/ViewLocator.cs` (45 LOC)
      - [x] `InteropProxy.SendInput2` — `WinTabber.Interop/InteropProxy.cs:834`; on the impl, not
            on `IInteropProxy`, called nowhere
- [x] **T1.4** Delete the two fully-commented-out `ProcessMonitor.cs` files — two dead copies of
      the same abandoned idea in different layers. Git has the history. *(F5)*
      - [x] `WinTabber.Events/ProcessMonitor.cs` (143 LOC, 90% commented)
      - [x] `WinTabber.API/ProcessMonitor.cs` (49 LOC, 81% commented)
- [x] **T1.5** Remove TUnit template scaffolding from `WinTabber.Infrastructure.Tests`: *(F10)*
      - [x] `Tests.cs`, `Tests2.cs`, `Tests3.cs`
      - [x] `Data/DataClass.cs`, `Data/DependencyInjectionClassConstructor.cs`
      - [x] `Data/DataSourceGenerator.cs` — referenced by nothing at all, not even the templates
      - [x] **Keep** `GlobalSetup.cs`'s `[assembly: Retry(3)]` — CLAUDE.md documents it as
            load-bearing. Strip only the template `Console.WriteLine` hook bodies.
      - Test count dropped from 133 → 81 (52 template tests removed), as expected.
- [x] **T1.6** Drop the dead `Microsoft.Windows.CsWin32` `PackageReference` from
      `WinTabber.Common.Util.csproj` — no `NativeMethods.txt`, no Win32 usage, generates nothing.
      Also the only CsWin32 ref in the repo missing `<PrivateAssets>all</PrivateAssets>`. *(F12)*
- [x] **T1.7** Remove the 10 empty `<Folder Include>` declarations: *(F15)*
      - `WinTabber.UI.Media/` — `Coordinators`, `Factories`, `Controls`
      - `WinTabberUI/` — `Extensions`, `Factories`, `ValueConverters`, `Infrastructure/NewFolder`
      - `WinTabber.Api.Media/` — `ShellApplications/Dtos`, `ShellApplications/Services`,
        `SMTC/Models`

### Deferred from Phase 1 — needs judgement, not deletion

- [x] **T1.8** Prune commented-out blocks in files that are still **live**. Each block was
      read individually before deciding delete-vs-strip. *(F5)*
      | File | Resolution |
      |---|---|
      | `WinTabber.UI.Media/ViewModels/DeviceItem.cs` | **Deleted whole file.** The review's 179/220 count excluded blank lines — every substantive line was commented (0 live code), and the type is referenced nowhere except a dead comment in `MediaControlsViewModel.cs` (also removed). |
      | `WinTabber.UI.Media/ViewModels/DeviceSessionWatcher.cs` | **Deleted whole file.** Same pattern — 100% dead once blank lines are excluded; unreferenced anywhere else. |
      | `WinTabber.UI.Media/ViewModels/AudioSession.cs` | **Deleted whole file.** Same pattern — 100% dead; only referenced by the also-dead `DeviceSessionWatcher.cs`. |
      | `WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs` | **Live** (WPF code-behind). Stripped a block of 7 commented-out `DependencyProperty` declarations — confirmed dead by checking `VolumeControls.xaml`, which binds `Volume`/`IsMuted`/etc. straight to the `DataContext` (the ViewModel), not to properties on the control itself. |
      | `WinTabberUI/Services/HintService.cs` | **Deleted whole file**, correcting the review: its only call site (`MediaControlsWindow.xaml.cs`) was itself commented out, so the class had zero live callers. Superseded by the `HintBehavior`/`IHintBehaviorKernel` system in `WinTabber.UI.Common`. Removed the dead call-site comment too. |
      | `WinTabber.Events/InputListenerService.cs` | **Live.** Stripped five dead private methods and two dead properties left over from the pre-SharpHook `Gma.System.MouseKeyHook`-based implementation; kept the live `SharpHook`-based `GetEvents`/`GetScheduler`. |
      | `Wintabber.SessionsTest/Program.cs` | **Live** (manual test console, disabled). Stripped the abandoned experimental session-joining code; kept the single live `Console.WriteLine("disabled")` line. |

      ⟵ *One file was missed here and pruned later, during Phase 3:*
      `WinTabber.Interop/NativeMethods.cs` — commented-out `DwmpActivateLivePreview` and
      `Dwm*Thumbnail*` `DllImport` stubs plus a fully commented `LivePreviewTrigger` enum
      (~52 lines, everything after the class body). The live class
      (`EnumerateProcessWindowHandles`, `ShouldIncludeWindow`, lazy `InvalidHwnds`) is untouched.

---

## Phase 2 — Extract `WinTabber.Infrastructure` *(F2 — highest leverage)*

Goal: `Infrastructure.Tests` stops referencing the WinExe.

- [x] **T2.1** Created `WinTabber.Infrastructure` (`net10.0-windows10.0.26100.0`, `UseWPF`), added
      to `WinTabber.slnx`.
- [x] **T2.2** Moved from `WinTabberUI/Infrastructure/`: `RadixTrie.cs`, `RadixNode.cs`,
      `HintTrie.cs`, `StringPool.cs`, `AppCache.cs` (namespaces left as `WinTabberUI.Infrastructure`
      — only the assembly moved, no consumer outside `WinTabberUI` referenced these types, so no
      call sites needed touching). Found during the move: `AppCache.Load()` was `internal`, which
      broke `BackgroundServiceContainer`/`Bootstrapper` once `WinTabberUI` became a separate
      assembly from the type — changed to `public`.
- [x] **T2.3** Moved from `WinTabberUI/Models/Settings/`: `ApplicationSettings.cs`,
      `GeneralSettings.cs`, `AppearanceSettings.cs`, `ShortcutSettings.cs`,
      `ShortcutCommandCatalog.cs` — plus `WinTabberUI/Paths.cs`. Namespaces unchanged.
      ⚠️ *Not in the original plan:* `GeneralSettings` depends on `StartupMode` and
      `ThumbnailResizeMode` (both in `WinTabberUI/Services/`) — moved those two zero-dependency
      enums to `WinTabber.Infrastructure/Settings/` as well (namespace kept as
      `WinTabberUI.Services`) to avoid a circular reference back to `WinTabberUI`.
- [x] **T2.4** `ShortcutCommandCatalog`'s two traps, both handled:
      - [x] moved `ShortcutCommands.json` + its `<EmbeddedResource>` item to the new project
      - [x] updated the `ResourceName` constant to `"WinTabber.Infrastructure.ShortcutCommands.json"`
      - [x] added the `iNKORE.UI.WPF.Modern` package reference to the new project
      - [x] verified the extension-method callers (`ShortcutsSettingsViewModel`, `WindowItem`,
            `ShortcutChip`) all live in `WinTabberUI`/`WinTabber.UI.*`, unaffected by the move.
      New project also needed `ReactiveUI`, `Microsoft-WindowsAPICodePack-Shell`, and
      `System.Runtime.Caching` package refs, plus `ProjectReference`s to `WinTabber.Api.Media`
      (for `InstalledApplicationInfo`, used by `AppCache`) and `WinTabber.Events` (for
      `ShortcutCommand`, used by `ShortcutSettings`/`ShortcutCommandCatalog`).
- [x] **T2.5** Repointed `WinTabber.Infrastructure.Tests` at `WinTabber.Infrastructure`; removed
      the `WinTabberUI` `ProjectReference`. Added `WinTabber.Events` and `WinTabber.Api.Media`
      alongside the existing `WinTabber.UI.Common` ref. Full suite: 81 passed, 0 failed.
- [x] **T2.6** Removed `[assembly: Retry(3)]` from `Infrastructure.Tests/GlobalSetup.cs` and ran
      the suite 5× standalone — stable every time (20/20). No longer needed now that the project
      doesn't pull in the WPF app.
- [x] **T2.7** `Wintabber.SessionsTest`: kept the project (a design doc,
      `docs/configurable-shortcuts-plan.md:508`, names it as the intended home for a future manual
      Hyperkey/capture test), but dropped its now-unused `WinTabberUI` and `WinTabber.API`
      `ProjectReference`s — `Program.cs` is just `Console.WriteLine("disabled")` and used neither.

---

## Phase 3 — Interop policy *(F1)*

Run **after** T1.3, which already removes 8 of the 11 stray `DllImport`s.

- [x] **T3.1** **Resolved: (b) — amend CLAUDE.md**, with a sharper boundary than the original
      framing. The rule is *what the call acts on*, not which layer it sits in: Win32 that
      observes/mutates **another process's** windows or processes goes through `IInteropProxy`;
      Win32 that affects the rendering of **our own** windows stays with the WPF code owning the
      `HwndSource`. Rationale:
      - **Testability.** The seam's value tracks the testability of the code behind it. The repo's
        only fake is `WinTabber.Api.Tests/Fakes/FakeInteropProxy.cs`, used by the `Suspension`
        tests — the process/window concern, exactly where the abstraction earns its keep. Zero
        tests touch `CloakHelper`/`PeekHelper`/`CornerHelper`, and they never will: their callers
        are WPF code-behind that needs a real `HwndSource` and message pump. An interface there
        permits only mock-verifies-the-mock tests.
      - **The thumbnail handle.** `DwmRegisterThumbnail` returns a handle whose lifetime is bound
        to a WPF control's. Behind `IInteropProxy` you either hand out raw handles (indirection
        with no encapsulation) or move control-lifecycle knowledge into `Interop`. Both worse than
        letting `WindowThumbnail.cs` own it.
      - **(a)'s real advantage, acknowledged:** it is mechanically enforceable (a banned-API
        analyzer on "no P/Invoke outside `Interop`"), where (b) needs judgement per call site.
        (b) can still be enforced with a two-entry directory allowlist. (a) would also become
        much more attractive *after* T5.1 — see the note there.
      - Not a portability seam: the app is pinned to `net10.0-windows10.0.26100.0` on WPF, so
        neither cross-platform nor headless end-to-end testing is on the table anyway.
- [x] **T3.2** Resolved by **deletion, not de-dup.** The DWM thumbnail set was generated 3× but
      had only **one** live consumer: `WinTabberUI/WindowThumbnail.cs`. The `Interop` copy was
      dead (referenced only by commented-out stubs) and the `UI.Common` copy had zero call sites —
      removed both; `WinTabberUI` keeps the live one. ⚠️ `RECT` **kept** in `Interop`: it reads as
      part of the thumbnail block but actually serves `GetWindowRect` / `Get`+`SetWindowPlacement` /
      `MoveWindow`.
- [x] **T3.3** Also resolved by **deletion, not de-dup.** Same shape as T3.2 — the only live
      consumer is `Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs`.
      The `UI.Common` and `WinTabberUI` copies had zero call sites; removed both. Also removed the
      six entries under `Api.Media`'s own `// not used?` marker (`PKEY_AppUserModel_ID`,
      `PKEY_Link_TargetParsingPath`, `BHID_PropertyStore`, `PropVariantToString`,
      `IEnumShellItems`, `BHID_EnumItems`) — the marker was right, all six are unreferenced.
- [x] **T3.6** Delete dead generated Win32 surface **not named by T3.2/T3.3**, found while scoping
      this phase. ⟵ *not in the original plan.*
      | File | Removed | Lines |
      |---|---|---:|
      | `WinTabber.UI.Common/NativeMethods.txt` | Everything except `DwmSetWindowAttribute` + `DWM_WINDOW_CORNER_PREFERENCE` (used by `Chrome/CloakHelper`, `PeekHelper`, `CornerHelper`) | 45 → 2 |
      | `WinTabberUI/NativeMethods.txt` | `GetApplicationUserModelId`, `SystemParametersInfoForDpi`, `GetProcessDpiAwareness`, `OpenProcess`, `PROCESS_ACCESS_RIGHTS`, `DwmSetWindowAttribute`, `DWM_WINDOW_CORNER_PREFERENCE`, plus the entire layered-window block (`Get`/`SetLayeredWindowAttributes`, `Get`/`SetWindowLong`, `RedrawWindow`, `UpdateLayeredWindow`, `PrintWindow`, `PRINT_WINDOW_FLAGS`, `PW_RENDERFULLCONTENT`) | 65 → 35 |
      | `WinTabber.Interop/NativeMethods.txt` | thumbnail set only (see T3.2) | 84 → 73 |
      Each layered-window symbol was checked **individually** — an earlier combined grep appeared
      to show the block was live, but it only matched `Views/ThumbnailWindow.xaml.cs` via the
      `WM_NCHITTEST` alternate in the same pattern. All nine are dead.
      Kept in `WinTabberUI`: `DwmIsCompositionEnabled` (`WindowThumbnail.cs:27`),
      `SystemParametersInfoA`+`W` (`Windowing/DesktopHelper.cs` calls `PInvoke.SystemParametersInfo`),
      and the `WMSZ_*`/`HT*`/`WM_NCHITTEST`/`WM_SIZING`/`WM_EXITSIZEMOVE` block
      (`Views/ThumbnailWindow.xaml.cs`).
- [x] **T3.4** Re-scoped by T3.1's decision — 1 of 3 resolved, 2 deferred to T5.1 (**deliberately
      left as-is for now**, they are not policy violations):
      - [x] `WinTabber.UI.Common/Chrome/Interop.cs:7` (`user32`, `SetWindowCompositionAttribute`) —
            **compliant under (b)**, no change needed. It affects our own window's rendering, and
            it is undocumented so CsWin32 has no metadata for it; hand-written is the only option.
      - [ ] `WinTabber.Infrastructure/AppCache.cs:129` (`gdi32`, `DeleteObject`) ⟵ *path corrected;
            the file moved out of `WinTabberUI/Infrastructure/` in Phase 2.*
      - [ ] `WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs:168`
            (`gdi32`, `DeleteObject`)
      The last two are a **verbatim duplicate** — identical
      `private static extern bool DeleteObject(IntPtr hObject)`, both freeing a GDI bitmap handle
      after converting a shell icon. That is a shared-utility problem, not an interop-policy one:
      the rule in T3.1 has no opinion on GDI resource cleanup, which acts on neither another
      process's windows nor our own rendering. Folded into T5.1 rather than forced into `Interop`.
      *(For the record, the hand-written `DllImport`s already inside `WinTabber.Interop` —
      `NtNativeMethods.cs`, `PInvoke.cs`'s `DwmpActivateLivePreview`, `UacHelper.cs` — are fine
      under either policy and were never in scope.)*
- [x] **T3.5** `CLAUDE.md`'s **Windows Interop** section rewritten to state the T3.1 rule, name
      which project owns which surface, legitimise hand-written `DllImport`s where CsWin32 has no
      metadata, and warn against adding `NativeMethods.txt` entries with no call site (CsWin32
      generates dependent types transitively, so explicit listings are usually unnecessary — the
      lesson from T3.2/T3.3/T3.6).

---

## Phase 4 — Mechanical cleanup

- [ ] **T4.1** Fix namespaces in library projects that declare the app's namespace — use
      IDE/Serena rename so call sites follow: *(F3)*
      | File | Current | Should be |
      |---|---|---|
      | `WinTabber.UI.Media/Services/MediaControlsStateService.cs` | `WinTabberUI.Services` | `WinTabber.UI.Media.Services` |
      | `WinTabber.UI.Media/ViewModels/DeviceItem.cs` | `WinTabberUI.ViewModels` | `WinTabber.UI.Media.ViewModels` |
      | `WinTabber.UI.Media/ViewModels/DeviceSessionWatcher.cs` | `WinTabberUI.ViewModels` | `WinTabber.UI.Media.ViewModels` |
      | `WinTabber.UI.Media/Views/MediaControlsWindow.xaml.cs` | `WinTabberUI` | `WinTabber.UI.Media.Views` |
      | `WinTabber.UI.Common/Chrome/CaptionButtons.xaml.cs` | `WinTabberUI.Chrome` | `WinTabber.UI.Common.Chrome` |
      *(`MediaSessionVm.cs` and `ControlledWindowBehavior.cs` are deleted in T1.3.)*
      ⚠️ `.xaml.cs` namespace changes must be matched in the paired `.xaml` `x:Class`.
- [ ] **T4.2** Fix two within-project namespace mismatches: *(F3)*
      - `WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDeviceRepository.cs` —
        `WinTabber.Api.Media.Repositories` → `...Api.Media.CoreAudio.Repositories`
      - `WinTabber.Api.Media/ShellApplications/Models/ThumbnailOptions.cs` —
        `...ShellApplications.Repositories` → `...ShellApplications.Models`
- [ ] **T4.3** `WinTabberUI/App.xaml.cs:57` — remove the redundant `ApplicationSettings.Load()`
      into an unused local. It contradicts the `Bootstrapper` comment that a second `Load()`
      "would silently diverge from what the user sees"; harmless today only because the result is
      discarded. Also handle the unused `startupService` local on the line above — either comment
      that it's resolved for its constructor side effect, or give it an explicit `.Init()`. *(F9)*
- [ ] **T4.4** Add `Directory.Build.props` for the properties repeated in every `.csproj`
      (`Nullable`, `ImplicitUsings`, `LangVersion`). Reconcile the three TFMs (`net10.0`,
      `net10.0-windows`, `net10.0-windows10.0.26100.0`) and the `<Platform>x64</Platform>` that
      is set in only 3 of 9 projects. *(F13)*
- [ ] **T4.5** Rename to remove the false parent/child implication between `WinTabber.API`
      (window registry) and `WinTabber.Api.Media` (audio/SMTC) — unrelated projects, no reference
      in either direction, inconsistent casing. Touches the `.slnx`, every `ProjectReference`, and
      every `using`. *(F14)*
- [ ] **T4.6** Consider thinning the `WinTabberUI` root — 18 loose top-level files
      (`HoverSelect`, `SpatialNavigationListView`, `WindowTileGrid`, `WindowTileInfo`,
      `WindowThumbnail`, `SysColor.xaml`, …). Contributes to the 6/10 cohesion score. *(Scorecard)*

---

## Phase 5 — Design work (plan separately)

- [ ] **T5.1** Split `IInteropProxy` — 39 members over 6 concerns. Suggested seams:
      `IProcessControl` (suspend/resume/elevation/image path), `IWindowPlacement`, and
      `IWindowInterop` for the rest. `InteropProxy` keeps implementing all three; consumers
      narrow. Unblocks a much smaller `FakeInteropProxy`. **Do after Phase 2.** *(F7)*
      > ⚠️ **Carries the two deferred items from T3.4.** When the split happens, resolve the
      > duplicated `DeleteObject` (`gdi32`) hand-written in both
      > `WinTabber.Infrastructure/AppCache.cs:129` and
      > `WinTabber.Api.Media/.../InstalledApplicationRepository.cs:168` — identical signature, same
      > purpose (freeing a GDI bitmap handle after a shell-icon conversion). It is GDI resource
      > cleanup, so T3.1's rule doesn't classify it; it wants a shared home, not `IInteropProxy`.
      > Both call sites do icon→bitmap conversion, so the natural fix is one small shared helper
      > rather than a new interface member.
      >
      > Also revisit T3.1 here. The main argument against policy **(a)** was that routing chrome
      > Win32 through `IInteropProxy` would grow an already-overloaded 39-member interface — an
      > objection this task removes. If after the split you want the stronger, mechanically
      > enforceable invariant ("no P/Invoke outside `Interop`", as a banned-API analyzer), this is
      > the point at which (a) becomes cheap to adopt. The counter-argument that survives the
      > split: an interface belongs **where its consumers are**, and the chrome consumers (plus
      > their untestable WPF surroundings) live in `UI.Common`/`WinTabberUI` — so concern-splitting
      > applied consistently still lands on (b)'s assembly layout.
- [ ] **T5.2** Make `BackgroundServiceContainer`'s load-bearing ordering explicit. Today
      `MediaDebugWindowCoordinator` must follow `MediaWindowViewCoordinator`, and
      `EnableDebugPrivilege()` must precede any suspend — enforced only by comments. Reordering
      two lines compiles, builds warning-free, and fails at runtime. Prefer constructor injection
      over shared-subject + ordering. **Keep the existing comments.** *(F8)*
- [ ] **T5.3** Add a test project for `WinTabber.Api.Media` (2,285 LOC — `IPolicyConfig` COM
      interop, WASAPI, STA scheduling; currently **zero** tests). *(F11)*
- [ ] **T5.4** Add test coverage for `WinTabber.Interop` (1,633 LOC, currently zero). *(F11)*

---

## Phase 6 — Already tracked in `docs/testability-future-work.md`

All four verified still open at `af16e91`. Listed here for completeness — the canonical
description lives in that doc.

- [ ] **T6.1** Add interfaces for the 5 concrete media-service registrations in `Bootstrapper.cs`
      (`CoreAudioDeviceRepository`, `AudioSessionService`, `AudioDeviceService`,
      `MediaSessionService`, `InstalledApplicationRepository`).
- [ ] **T6.2** Restrict `Ioc.Default` to startup; use constructor injection. **17 call sites**
      across `WinTabberUI` and `WinTabber.UI.Media`.
- [ ] **T6.3** Move constructor-time Rx subscriptions to `WhenActivated` / `Initialize()`.
      `AudioDeviceSelectorViewModel` lines 57/61/100 still subscribe in the constructor with no
      `CompositeDisposable`; `MediaControlsViewModel` has partially adopted `.DisposeWith(_cleanUp)`.
- [ ] **T6.4** Fix `static WeakReference<FrameworkElement>? _activeRootRef` at
      `WinTabber.UI.Common/Behaviors/HintBehavior.cs:161` — shared across test runs.

---

## Summary

| Phase | Tasks | Effort | Risk |
|---|---:|---|---|
| 0 — Prep | 3 | Trivial | None |
| 1 — Deletion | 8 | Low | Low — T1.8 needs judgement |
| 2 — Extract Infrastructure | 7 | Medium | Medium — T2.4 is the trap |
| 3 — Interop policy | 6 | Medium | Medium — T3.1 gates the rest |
| 4 — Mechanical | 6 | Low–Med | Low — T4.1/T4.5 are wide renames |
| 5 — Design | 4 | Medium–High | Plan separately |
| 6 — Tracked | 4 | Medium | Already scoped in `docs/` |

**Total: 38 tasks.** Phases 1–2 deliver most of the value; 5–6 are genuine design work.
