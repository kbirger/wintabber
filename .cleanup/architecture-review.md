# Architecture Review — WinTabber

**Date:** 2026-09-03
**Branch:** `dev` @ `af16e91`
**Scope:** 13 solution projects + 2 orphans, 23,812 LOC C#, 30 XAML.
**Build verified:** `dotnet build WinTabber.slnx` → 0 warnings, 0 errors.

---

## 1. Structure & dependency graph

Layer-based, and the project graph is **acyclic** — no circular project references.

```
WinTabberUI (WinExe, 7,471 LOC, 31%)
├── WinTabber.UI.Media ──┬── WinTabber.Api.Media ── Common.Util
│                        ├── WinTabber.UI.Common ──┬── Common.Util
│                        │                         └── WinTabber.Events
│                        ├── WinTabber.Events ── WinTabber.Interop ── Common.Util
│                        └── WinTabber.Interop
├── WinTabber.API ── WinTabber.Interop
├── WinTabber.Api.Media          (does NOT reference Interop — has its own CsWin32)
├── WinTabber.UI.Common
└── WinTabber.Events

WinTabber.Generators (netstandard2.0)  → analyzer ref from API, Api.Media, UI.Media, WinTabberUI
```

Test / manual projects:

```
WinTabber.Api.Tests            → API, Interop
WinTabber.Events.Tests         → Events
WinTabber.Infrastructure.Tests → UI.Common, WinTabberUI   ← see F2
Wintabber.SessionsTest         → API, WinTabberUI          ← see F2
```

### Coupling metrics

| Project | LOC | Ca (depended on by) | Ce (depends on) | Instability |
|---|---:|---:|---:|---:|
| Common.Util | 91 | 5 | 0 | 0.00 |
| Interop | 1,633 | 4 | 1 | 0.20 |
| Events | 2,520 | 4 | 1 | 0.20 |
| API | 1,704 | 3 | 1 | 0.25 |
| Api.Media | 2,285 | 2 | 1 | 0.33 |
| UI.Common | 3,461 | 3 | 2 | 0.40 |
| UI.Media | 2,377 | 1 | 6 | 0.86 |
| **WinTabberUI** | **7,471** | **2** | **7** | **0.78** |

The stable-abstractions direction is right: the leaf utilities are stable, the app shell is
unstable. The one edge that breaks it is `Ca=2` on `WinTabberUI` — see finding **F2**.

### Patterns identified and correctly applied

- **DI composition root** — `WinTabberUI/Bootstrapper.cs`
- **Facade** — `IInteropProxy`
- **Repository** — `ProcessRepository`, `CoreAudioSessionRepository`, `SMTCSessionRepository`,
  `InstalledApplicationRepository`
- **Strategy** — `ISuspensionStrategy` (two implementations, multi-registered)
- **Factory** — `ApplicationStateViewModelFactory`, `MediaSessionViewModelFactory`,
  `AudioDeviceSelectorViewModelFactory`, `WindowSelectorWindowFactory`
- **Template Method** — `ViewCoordinatorBase<T>`
- **Observer** — Rx / DynamicData throughout
- **Source generator** — `[Lazy]` (used in 10 places)

### What's genuinely good

`ViewCoordinatorBase<T>` is a clean abstraction — window lifecycle, external-close tracking, and
the `IsInstanceShown` override hook for animated windows are handled in one place across 9
coordinators.

`WindowManager` is 122 lines and delegates properly — not the god class the name invites.

The `WinTabber.Events.Shortcuts` model is deliberately WPF- and SharpHook-free, with the reason
documented in `WinTabber.UI.Common.csproj`. That constraint is what lets `WinTabber.Events.Tests`
exist at all.

The load-bearing comments in `Bootstrapper` and `BackgroundServiceContainer` explain *why*, not
*what*.

---

## 2. Findings

### HIGH

#### F1 — The Win32 interop boundary documented in CLAUDE.md doesn't hold

**Location:** 4 × `NativeMethods.txt`; `WinTabberUI/WindowHelper2.cs`,
`WinTabberUI/Infrastructure/AppCache.cs:129`, `WinTabber.UI.Common/Chrome/Interop.cs:7`,
`WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs:168`

`CLAUDE.md` states: *"All Win32 calls go through `IInteropProxy`… Never call P/Invoke directly
from other projects."* In practice there are **four** CsWin32 generation sites and **11
hand-written `DllImport`s outside `WinTabber.Interop`**:

| `NativeMethods.txt` | Notes |
|---|---|
| `WinTabber.Interop/` | the sanctioned one |
| `WinTabber.UI.Common/` | 26-line block **byte-identical** to `WinTabberUI/`'s first 26 lines |
| `WinTabberUI/` | superset of UI.Common's |
| `WinTabber.Api.Media/` | Shell / `IShellItem` block, duplicated from the other two |

The DWM thumbnail set (`DwmRegisterThumbnail`, `DwmUnregisterThumbnail`,
`DWM_THUMBNAIL_PROPERTIES`, the five `DWM_TNP_*` flags, `DwmQueryThumbnailSourceSize`) is
generated **three separate times** into three assemblies as three incompatible types. The
Shell/AUMID set (`SHGetKnownFolderItem`, `IShellItem`, `IPropertyStore`,
`PKEY_AppUserModel_ID`, `IShellItemImageFactory`, …) is generated three times.

Raw `DllImport` counts: `WindowHelper2.cs` (8), `AppCache.cs` (1), `Chrome/Interop.cs` (1),
`InstalledApplicationRepository.cs` (1).

**Impact:** the same native concept has three unrelated managed types, so thumbnail or DPI logic
can't move between layers without re-marshalling; `IInteropProxy` isn't actually a seam you can
fake at, because three projects bypass it entirely.

**Recommendation:** pick one and commit to it.
- *Enforce:* route the DWM/Shell/DPI calls through `Interop`, delete the other three
  `NativeMethods.txt`.
- *Amend:* update CLAUDE.md to say `Interop` owns *process and window-manager* Win32 while
  presentation-layer chrome owns its own — and still de-dupe the DWM block shared verbatim
  between `UI.Common` and `WinTabberUI`.

**Effort:** Medium. **Priority:** High.

> Note: **F6**'s deletion of `WindowHelper2.cs` alone removes 8 of the 11 stray `DllImport`s. Do
> F6 first.

---

#### F2 — Two projects take a `ProjectReference` on `WinTabberUI`, the WinExe

**Location:** `WinTabber.Infrastructure.Tests/WinTabber.Infrastructure.Tests.csproj`,
`Wintabber.SessionsTest/Wintabber.SessionsTest.csproj`

Both reference the application executable. The consequence is visible in the test project's own
name: "Infrastructure.Tests" has to pull in the entire WPF app — `App.xaml`, all 21 XAML
resource dictionaries, `H.NotifyIcon`, `TaskScheduler`, `NAudio` — to reach `RadixTrie`,
`HintTrie`, `StringPool` and `ShortcutSettings`. That is also why it needs the 3× retry policy
in `GlobalSetup.cs`.

**Impact:** the infrastructure primitives can't be tested in isolation, and 31% of the codebase
sits in a project nothing can depend on cleanly.

**Recommendation:** extract a `WinTabber.Infrastructure` library containing:
- `WinTabberUI/Infrastructure/` — `RadixTrie`, `RadixNode`, `HintTrie`, `StringPool`,
  `AppCache`, `AumidHelpers`
- `WinTabberUI/Models/Settings/` — `ApplicationSettings`, `GeneralSettings`,
  `AppearanceSettings`, `ShortcutSettings`, `ShortcutCommandCatalog`
- `WinTabberUI/Paths.cs` (moves with the settings)

Then point `Infrastructure.Tests` at that library and drop the `WinTabberUI` reference.

**Effort:** Medium. **Priority:** High — *this is the single highest-leverage change in the
repo.* It fixes the project naming, the test isolation, and the `Ca=2` on the exe at once.

---

### MEDIUM

#### F3 — Library projects declare the application's namespace

Eight files in `WinTabber.UI.Media` / `WinTabber.UI.Common` sit under `namespace WinTabberUI.*`:

```
WinTabber.UI.Media/Services/MediaControlsStateService.cs   → WinTabberUI.Services
WinTabber.UI.Media/ViewModels/DeviceItem.cs                → WinTabberUI.ViewModels
WinTabber.UI.Media/ViewModels/DeviceSessionWatcher.cs      → WinTabberUI.ViewModels
WinTabber.UI.Media/ViewModels/MediaSessionVm.cs            → WinTabberUI.ViewModels
WinTabber.UI.Media/Views/MediaControlsWindow.xaml.cs       → WinTabberUI
WinTabber.UI.Common/Behaviors/ControlledWindowBehavior.cs  → WinTabberUI.Behaviors
WinTabber.UI.Common/Chrome/CaptionButtons.xaml.cs          → WinTabberUI.Chrome
```

Leftover from the extraction of these projects out of the app. It makes
`using WinTabberUI.Services;` ambiguous about which assembly you're binding to, and hides the
layer relationship from readers.

Two more are wrong within their own project:
- `WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDeviceRepository.cs` declares
  `WinTabber.Api.Media.Repositories` (missing `.CoreAudio`)
- `WinTabber.Api.Media/ShellApplications/Models/ThumbnailOptions.cs` declares
  `...ShellApplications.Repositories`

**Effort:** Low (rename-safe).

---

#### F4 — Two dead projects tracked in git, neither in the solution

- `WinTabber/` — `Program.cs` and `Main.cs`, **both 100% commented out** (88 LOC total).
- `WinTabber.GameBar/` — a `.csproj` with **zero source files**, targeting **net9.0** and pinning
  `Microsoft.Windows.CsWinRT 2.2.0` / `System.Reactive 6.1.0` inline, bypassing
  `Directory.Packages.props`.

Neither is in `WinTabber.slnx`, so neither is covered by the clean-warning state.

**Recommendation:** delete both. **Effort:** Low.

---

#### F5 — ~2,600 lines (11% of the codebase) of commented-out code, concentrated in one layer

Excluding XML doc comments (1,331 lines, legitimate), non-doc comment lines total 2,610. The
distribution is not uniform:

| File | Commented / total | % |
|---|---:|---:|
| `WinTabber.Events/ProcessMonitor.cs` | 129/143 | 90% ← 100% dead |
| `Wintabber.SessionsTest/Program.cs` | 115/140 | 82% |
| `WinTabber.UI.Media/ViewModels/MediaSessionVm.cs` | 102/123 | 82% ← also unreferenced |
| `WinTabber.UI.Common/Behaviors/ControlledWindowBehavior.cs` | 63/77 | 81% ← also unreferenced (F6) |
| `WinTabber.API/ProcessMonitor.cs` | 40/49 | 81% ← 100% dead |
| `WinTabber.UI.Media/ViewModels/DeviceItem.cs` | 179/220 | 81% |
| `WinTabber.UI.Media/ViewModels/DeviceSessionWatcher.cs` | 106/131 | 80% |
| `WinTabber.UI.Media/ViewModels/AudioSession.cs` | 185/242 | 76% |
| `WinTabber/Program.cs` | 37/56 | 66% |
| `WinTabber.UI.Media/UserControls/VolumeControls.xaml.cs` | 77/126 | 61% |
| `WinTabberUI/Services/HintService.cs` | 86/151 | 56% |
| `WinTabber.Events/InputListenerService.cs` | 64/160 | 40% |

Both `ProcessMonitor.cs` files are entirely commented out — two dead copies of the same
abandoned idea sitting in two different layers. Git has the history; these files don't need to.

**Effort:** Low.

---

#### F6 — Orphan types (referenced by nothing but themselves)

| Type | File | LOC |
|---|---|---:|
| `WindowHelper2` | `WinTabberUI/WindowHelper2.cs` | 289 ← carries 8 of the 11 stray `DllImport`s |
| `AumidHelpers` | `WinTabberUI/Infrastructure/AumidHelpers.cs` | 223 ← all four static methods uncalled |
| `MediaSessionVm` | `WinTabber.UI.Media/ViewModels/MediaSessionVm.cs` | 123 |
| `ControlledWindowBehavior` | `WinTabber.UI.Common/Behaviors/ControlledWindowBehavior.cs` | 77 ← also 81% commented |
| `SelectionList` + `ISelectable` | `WinTabber.UI.Media/ViewModels/` | 48 |
| `StaThreadHost` | `WinTabberUI/Interop/StaThreadHost.cs` | 46 |
| `ViewLocator` | `WinTabberUI/Infrastructure/ViewLocator.cs` | 45 |
| `InteropProxy.SendInput2` | `WinTabber.Interop/InteropProxy.cs:834` | — declared on the impl, absent from `IInteropProxy`, called nowhere |

`AumidHelpers` and `ControlledWindowBehavior` were added on a second pass while breaking this
review into tasks; both were missed by the first sweep (the former is not commented out, so it
reads as live code).

> **Counter-example — do not delete by name search alone.** `ShortcutCommandCatalog` looks
> identical to these under a name-based scan: the type name appears only in its own file. It is
> very much alive — `GetDisplayName` / `GetDescription` / `GetGroupName` / `GetIcon` are
> **extension methods on `ShortcutCommand`**, so call sites (`ShortcutsSettingsViewModel`,
> `WindowItem`, `ShortcutChip`) never name the type. Confirm by member name, not type name.

**Effort:** Low.

---

#### F7 — `IInteropProxy` is a 39-member facade over six unrelated concerns

**Location:** `WinTabber.Interop/IInteropProxy.cs`

Window geometry/placement, foreground & activation, process identity/image path, elevation,
**process and thread suspension**, synthetic input, and DWM live preview all sit on one
interface.

Interface Segregation violations:
- `ProcessSuspensionService` and both `ISuspensionStrategy` implementations need
  `SuspendProcess` / `ResumeProcess` / `SuspendProcessThreads` / `ResumeProcessThreads` — and
  take all 39.
- `WindowThumbnailService` needs the placement calls — and takes all 39.
- `FakeInteropProxy` in the test project must stub the whole surface for every test.

**Recommendation:** split off `IProcessControl` (suspend/resume/elevation/image path) and
`IWindowPlacement` from `IWindowInterop`. `InteropProxy` keeps implementing all three; consumers
depend only on what they call.

**Effort:** Medium. **Priority:** do this *after* F2, not before.

---

#### F8 — Startup ordering is implicit and enforced only by comments

**Location:** `WinTabberUI/BackgroundServiceContainer.cs`

The constructor is a service-locator sequence of 12 eager `GetRequiredService` calls whose order
is load-bearing:
- `EnableDebugPrivilege()` — *"Must happen before any suspend attempt"*
- `MediaDebugWindowCoordinator` — *"Must come after MediaWindowViewCoordinator"*

The comments are good and should stay. But nothing enforces the constraint: reordering two lines
compiles, builds warning-free, and fails at runtime.

**Recommendation:** make the debug window's dependency on the media window explicit (constructor
injection rather than a shared visibility subject plus ordering), so the container's own
resolution order guarantees it.

**Effort:** Medium.

---

### LOW

#### F9 — Redundant `ApplicationSettings.Load()` in `App.OnStartup`

`WinTabberUI/App.xaml.cs:57` calls `ApplicationSettings.Load()` into a local that is never read —
directly against the `Bootstrapper` comment that a *"second Load() elsewhere would silently
diverge from what the user sees."* Harmless today only because the result is discarded.
`startupService` on the line above is also unused (resolved for its constructor side effect —
worth a comment saying so, or an explicit `.Init()`).

#### F10 — TUnit scaffolding still shipped

`WinTabber.Infrastructure.Tests/Tests.cs`, `Tests2.cs`, `Tests3.cs` and the `Data/` folder
supporting them are the template's *"For more information, check out the documentation /
https://tunit.dev/"* samples. They pad the suite with passing tests that assert nothing about
WinTabber.

#### F11 — Test coverage is missing exactly where the risk is

`WinTabber.Api.Media` (2,285 LOC — the most COM/WASAPI-heavy code in the repo, with
`IPolicyConfig` COM interop and STA scheduling) and `WinTabber.Interop` (1,633 LOC) have **no
test project at all**. The three suites cover `Events.Shortcuts`, `API.Suspension`, and the
trie/settings primitives.

#### F12 — Dead CsWin32 reference in `WinTabber.Common.Util`

References `Microsoft.Windows.CsWin32` but has no `NativeMethods.txt` and no Win32 usage — the
package generates nothing. It's also the only CsWin32 reference in the repo without
`<PrivateAssets>all</PrivateAssets>`. Drop it.

#### F13 — No `Directory.Build.props`

Three target frameworks across nine projects (`net10.0`, `net10.0-windows`,
`net10.0-windows10.0.26100.0`) with no shared props file. `Nullable` / `ImplicitUsings` /
`Platform` are repeated in every `.csproj`, and `<Platform>x64</Platform>` is set in only 3 of
them.

#### F14 — `WinTabber.API` vs `WinTabber.Api.Media` naming

Unrelated projects whose names imply a parent/child relationship. They share no reference in
either direction. Casing differs too (`.API` vs `.Api`).

#### F15 — Empty declared folders

10 empty folders declared via `<Folder Include>` across four `.csproj` files:

```
WinTabber.UI.Media/{Coordinators,Factories,Controls}
WinTabberUI/{Extensions,Factories,ValueConverters,Infrastructure/NewFolder}
WinTabber.Api.Media/{ShellApplications/Dtos,ShellApplications/Services,SMTC/Models}
```

---

## 3. Scorecard

| Aspect | Score | Notes |
|---|---:|---|
| Layer separation | 7/10 | Correct direction, acyclic, but the interop boundary leaks (F1) and namespaces don't match assemblies (F3) |
| Coupling | 6/10 | Leaves are stable; the exe is a dependency (F2) and `IInteropProxy` is over-broad (F7) |
| Cohesion | 6/10 | Strong in `Events.Shortcuts` and `API.Suspension`; weak in `UI.Media` (F5) and the `WinTabberUI` root (18 loose files) |
| SOLID | 6/10 | DI, Strategy, and factories are real; ISP (F7) and DIP (concrete registrations) are the gaps |
| Dead code | 4/10 | 11% commented-out, 2 dead projects, 6 orphan types, 3 template test files |
| Testability | 6/10 | Good fakes and a WPF-free shortcut model; no coverage of the COM layer, tests coupled to the exe |
| Build hygiene | 9/10 | 0 warnings, central package management, CSharpier configured |

---

## 4. Suggested order

1. **F4 + F5 + F6 + F10 + F12 + F15** — pure deletion, no design decisions, one commit. Removes
   ~1,400 lines and most of the noise the rest of this review has to look past.
2. **F2** — extract `WinTabber.Infrastructure`. Unblocks real infrastructure testing and fixes
   the exe-as-library edge.
3. **F1** — decide the interop policy and either enforce it or amend CLAUDE.md. (F6 already
   removed 8 of the 11 stray `DllImport`s.)
4. **F3, F9, F13, F14** — mechanical cleanup.
5. **F7, F8, F11** — design work; worth planning separately.

---

## 5. Already tracked elsewhere

`docs/testability-future-work.md` covers four items that overlap this review's territory. All
four were **verified still open** as of `af16e91` and are deliberately *not* restated above:

| Item | Verified state |
|---|---|
| Concrete-type DI registrations (`CoreAudioDeviceRepository`, `AudioSessionService`, `AudioDeviceService`, `MediaSessionService`, `InstalledApplicationRepository`) | Still concrete in `Bootstrapper.cs` |
| `Ioc.Default` as a global static container | 17 call sites across `WinTabberUI` and `UI.Media` |
| Constructor-time VM subscriptions | `AudioDeviceSelectorViewModel` lines 57/61/100 still subscribe in the constructor with no `CompositeDisposable`; `MediaControlsViewModel` has partially adopted `.DisposeWith(_cleanUp)` |
| `static WeakReference<FrameworkElement>? _activeRootRef` | Still present at `WinTabber.UI.Common/Behaviors/HintBehavior.cs:161` |
