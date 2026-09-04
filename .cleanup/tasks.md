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

- [ ] **T1.1** Delete dead project `WinTabber/` (`Program.cs`, `Main.cs` — both 100% commented
      out, 88 LOC). Not in `WinTabber.slnx`. *(F4)*
- [ ] **T1.2** Delete dead project `WinTabber.GameBar/` — `.csproj` only, zero source files,
      targets net9.0, pins `CsWinRT 2.2.0` / `System.Reactive 6.1.0` inline against
      `Directory.Packages.props`. *(F4)*
- [ ] **T1.3** Delete orphan types — none referenced outside their own file: *(F6)*
      - [ ] `WinTabberUI/WindowHelper2.cs` (289 LOC, **8 `DllImport`s**)
      - [ ] `WinTabberUI/Infrastructure/AumidHelpers.cs` (223 LOC — all four static methods
            uncalled) ⟵ *not in the original F6 list; found during task breakdown*
      - [ ] `WinTabber.UI.Media/ViewModels/MediaSessionVm.cs` (123 LOC, 82% commented)
      - [ ] `WinTabber.UI.Common/Behaviors/ControlledWindowBehavior.cs` (77 LOC, 81% commented)
            ⟵ *not in the original F6 list; found during task breakdown*
      - [ ] `WinTabber.UI.Media/ViewModels/SelectionList.cs` + `ISelectable.cs` (48 LOC)
      - [ ] `WinTabberUI/Interop/StaThreadHost.cs` (46 LOC)
      - [ ] `WinTabberUI/Infrastructure/ViewLocator.cs` (45 LOC)
      - [ ] `InteropProxy.SendInput2` — `WinTabber.Interop/InteropProxy.cs:834`; on the impl, not
            on `IInteropProxy`, called nowhere
- [ ] **T1.4** Delete the two fully-commented-out `ProcessMonitor.cs` files — two dead copies of
      the same abandoned idea in different layers. Git has the history. *(F5)*
      - [ ] `WinTabber.Events/ProcessMonitor.cs` (143 LOC, 90% commented)
      - [ ] `WinTabber.API/ProcessMonitor.cs` (49 LOC, 81% commented)
- [ ] **T1.5** Remove TUnit template scaffolding from `WinTabber.Infrastructure.Tests`: *(F10)*
      - [ ] `Tests.cs`, `Tests2.cs`, `Tests3.cs`
      - [ ] `Data/DataClass.cs`, `Data/DependencyInjectionClassConstructor.cs`
      - [ ] `Data/DataSourceGenerator.cs` — referenced by nothing at all, not even the templates
      - [ ] **Keep** `GlobalSetup.cs`'s `[assembly: Retry(3)]` — CLAUDE.md documents it as
            load-bearing. Strip only the template `Console.WriteLine` hook bodies.
- [ ] **T1.6** Drop the dead `Microsoft.Windows.CsWin32` `PackageReference` from
      `WinTabber.Common.Util.csproj` — no `NativeMethods.txt`, no Win32 usage, generates nothing.
      Also the only CsWin32 ref in the repo missing `<PrivateAssets>all</PrivateAssets>`. *(F12)*
- [ ] **T1.7** Remove the 10 empty `<Folder Include>` declarations: *(F15)*
      - `WinTabber.UI.Media/` — `Coordinators`, `Factories`, `Controls`
      - `WinTabberUI/` — `Extensions`, `Factories`, `ValueConverters`, `Infrastructure/NewFolder`
      - `WinTabber.Api.Media/` — `ShellApplications/Dtos`, `ShellApplications/Services`,
        `SMTC/Models`

### Deferred from Phase 1 — needs judgement, not deletion

- [ ] **T1.8** Prune commented-out blocks in files that are still **live**. These can't be
      deleted wholesale; each block needs a read. *(F5)*
      | File | Commented / total |
      |---|---:|
      | `WinTabber.UI.Media/ViewModels/DeviceItem.cs` | 179/220 (81%) |
      | `WinTabber.UI.Media/ViewModels/DeviceSessionWatcher.cs` | 106/131 (80%) |
      | `WinTabber.UI.Media/ViewModels/AudioSession.cs` | 185/242 (76%) |
      | `WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs` | 77/126 (61%) |
      | `WinTabberUI/Services/HintService.cs` | 86/151 (56%) — **live**, used by `MediaControlsWindow.xaml.cs` |
      | `WinTabber.Events/InputListenerService.cs` | 64/160 (40%) |
      | `Wintabber.SessionsTest/Program.cs` | 115/140 (82%) |

---

## Phase 2 — Extract `WinTabber.Infrastructure` *(F2 — highest leverage)*

Goal: `Infrastructure.Tests` stops referencing the WinExe.

- [ ] **T2.1** Create `WinTabber.Infrastructure` (`net10.0-windows10.0.26100.0`), add to
      `WinTabber.slnx`.
- [ ] **T2.2** Move from `WinTabberUI/Infrastructure/`: `RadixTrie.cs`, `RadixNode.cs`,
      `HintTrie.cs`, `StringPool.cs`, `AppCache.cs`.
      *(`AumidHelpers.cs` is deleted in T1.3, not moved.)*
- [ ] **T2.3** Move from `WinTabberUI/Models/Settings/`: `ApplicationSettings.cs`,
      `GeneralSettings.cs`, `AppearanceSettings.cs`, `ShortcutSettings.cs`,
      `ShortcutCommandCatalog.cs` — plus `WinTabberUI/Paths.cs`, which `ApplicationSettings`
      depends on.
- [ ] **T2.4** ⚠️ **`ShortcutCommandCatalog` has two traps.** It reads the embedded resource
      `"WinTabberUI.Resources.ShortcutCommands.json"` via `Assembly.GetExecutingAssembly()`, and
      it depends on `FluentSystemIcons` (iNKORE) for `GetIcon`. Moving it requires:
      - [ ] move `Resources/ShortcutCommands.json` + its `<EmbeddedResource>` item to the new project
      - [ ] update the `ResourceName` constant to the new assembly's namespace
      - [ ] add the `iNKORE.UI.WPF.Modern` package reference to the new project
      - [ ] its `Get*` members are **extension methods on `ShortcutCommand`** — the type name
            never appears at call sites, so a name-based search will wrongly report it as unused.
            Callers: `ShortcutsSettingsViewModel`, `WindowItem`, `ShortcutChip`.
- [ ] **T2.5** Repoint `WinTabber.Infrastructure.Tests` at the new project; **remove the
      `WinTabberUI` `ProjectReference`**. The three real test files also need `WinTabber.Events`,
      `WinTabber.UI.Common`, and `WinTabber.Api.Media` — keep/add those refs.
- [ ] **T2.6** Re-evaluate the `[assembly: Retry(3)]` in `Infrastructure.Tests/GlobalSetup.cs`.
      It exists because the suite drags in the whole WPF app; once decoupled, try removing it and
      see whether the suite is stable.
- [ ] **T2.7** Decide what to do with `Wintabber.SessionsTest` — the other `WinTabberUI`
      consumer. Options: repoint at the new library, or delete (its `Program.cs` is 82%
      commented out).

---

## Phase 3 — Interop policy *(F1)*

Run **after** T1.3, which already removes 8 of the 11 stray `DllImport`s.

- [ ] **T3.1** **Decide the policy first** — everything below depends on it:
      - **(a) Enforce** CLAUDE.md as written: all Win32 routes through `IInteropProxy`; delete
        the three non-`Interop` `NativeMethods.txt`.
      - **(b) Amend** CLAUDE.md: `Interop` owns process/window-manager Win32; presentation-layer
        chrome owns its own. Still de-dupe the shared blocks.
- [ ] **T3.2** De-dupe the DWM thumbnail set — currently generated **3×** into 3 assemblies as 3
      incompatible types (`Interop`, `UI.Common`, `WinTabberUI`): `DwmRegisterThumbnail`,
      `DwmUnregisterThumbnail`, `DwmUpdateThumbnailProperties`, `DWM_THUMBNAIL_PROPERTIES`,
      `RECT`, the five `DWM_TNP_*` flags, `DwmQueryThumbnailSourceSize`.
      *(Required under either policy.)*
- [ ] **T3.3** De-dupe the Shell/AUMID set — generated 3× (`Api.Media`, `UI.Common`,
      `WinTabberUI`): `SHGetKnownFolderItem`, `IShellItem`, `IPropertyStore`,
      `PKEY_AppUserModel_ID`, `IShellItemImageFactory`, `SHCreateItemFromParsingName`, …
      Note `Api.Media`'s copy has entries already marked `// not used?`.
- [ ] **T3.4** Resolve the 3 remaining hand-written `DllImport`s:
      - [ ] `WinTabber.UI.Common/Chrome/Interop.cs:7` (`user32`)
      - [ ] `WinTabberUI/Infrastructure/AppCache.cs:129` (`gdi32`)
      - [ ] `WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs:168` (`gdi32`)
- [ ] **T3.5** Update `CLAUDE.md` to match whatever T3.1 decided, so the doc and the code agree.

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
| 3 — Interop policy | 5 | Medium | Medium — T3.1 gates the rest |
| 4 — Mechanical | 6 | Low–Med | Low — T4.1/T4.5 are wide renames |
| 5 — Design | 4 | Medium–High | Plan separately |
| 6 — Tracked | 4 | Medium | Already scoped in `docs/` |

**Total: 37 tasks.** Phases 1–2 deliver most of the value; 5–6 are genuine design work.
