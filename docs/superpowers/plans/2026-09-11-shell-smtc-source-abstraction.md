# Shell/SMTC Source Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the last two genuinely-open items from `.todos/testability-review.md` (#9, #10) —
wrap `InstalledApplicationRepository`'s static Shell calls and `SMTCSessionRepository`'s static SMTC
call each behind a small injectable interface, so each repository's *acquisition* step (not its
downstream session/shell-item processing, which stays out of scope — see Global Constraints) is
substitutable in a unit test.

**Architecture:** Two independent, same-shaped seams, one per repository:
`ISmtcSessionSource.RequestAsync()` replaces the static
`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` call;
`IShellApplicationSource.GetAppsFolder()`/`CreateShellItemImageFactory(string)` replace the static
`KnownFolderHelper.FromKnownFolderId()`/`PInvoke.SHCreateItemFromParsingName()` calls. Both follow
the existing `IMMDeviceEnumeratorWrapper`/`IPolicyConfigClientWrapper` pattern in
`WinTabber.Api.Media/CoreAudio/`: interface + concrete wrapper living beside the repository's
namespace root, injected via constructor, registered in `WinTabberUI/Bootstrapper.cs`.

**Tech Stack:** C# 12 (primary constructors), System.Reactive, DynamicData, TUnit, CsWin32
(`Windows.Win32.UI.Shell`), WinRT (`Windows.Media.Control`).

**Spec:** `.todos/testability-review.md` items 9 and 10 (both already updated in place with
verification notes as of 2026-09-11 — this plan implements the remaining unchecked boxes under
those two headings).

## Global Constraints

- Both `GlobalSystemMediaTransportControlsSession`/`GlobalSystemMediaTransportControlsSessionManager`
  (WinRT) and `ShellObject` (Microsoft.WindowsAPICodePack.Shell) are sealed/COM-backed types with no
  accessible test constructor — confirmed during investigation, same class of problem `MMDevice` had
  before the `IAudioDevice` abstraction. Faking full session/shell-item *processing* is out of scope
  for this plan (would need its own design pass, per the existing `WinTabber.Api.Media.Tests/README.md`
  "Not covered, and why" section). This plan only makes the *acquisition* step substitutable, and each
  task's test proves exactly that: an acquisition failure propagates correctly rather than hanging or
  crashing unobserved.
- Match existing project conventions exactly: interface + concrete impl live as sibling files at the
  root of the feature's namespace folder (see `WinTabber.Api.Media/CoreAudio/IMMDeviceEnumeratorWrapper.cs`
  next to `MMDeviceEnumeratorWrapper.cs`), not inside the `Repositories/` subfolder.
- `dotnet build WinTabber.slnx` must stay at 0 warnings; `dotnet test --solution WinTabber.slnx` must
  stay green throughout (per `CLAUDE.md`).
- No other file in the tree constructs `SMTCSessionRepository` or `InstalledApplicationRepository`
  directly (`new SMTCSessionRepository(...)` / `new InstalledApplicationRepository(...)`) outside
  `WinTabberUI/Bootstrapper.cs` — confirmed via a whole-tree search. Only the Bootstrapper registration
  needs updating alongside each constructor change.

---

## Task 1: `ISmtcSessionSource` — inject into `SMTCSessionRepository`

**Files:**
- Create: `WinTabber.Api.Media/SMTC/ISmtcSessionSource.cs`
- Create: `WinTabber.Api.Media/SMTC/SmtcSessionSource.cs`
- Create: `WinTabber.Api.Media.Tests/Fakes/FakeSmtcSessionSource.cs`
- Create: `WinTabber.Api.Media.Tests/SMTC/SMTCSessionRepositoryTests.cs`
- Modify: `WinTabber.Api.Media/SMTC/Repositories/SMTCSessionRepository.cs`
- Modify: `WinTabberUI/Bootstrapper.cs:1-8` (usings), `:105` (registration)

**Interfaces:**
- Produces: `ISmtcSessionSource.RequestAsync() : Task<GlobalSystemMediaTransportControlsSessionManager>`
  — the seam `SMTCSessionRepository`'s constructor now takes, and what `FakeSmtcSessionSource`
  implements for tests.

- [ ] **Step 1: Create the interface** (needed for the test below to compile)

`WinTabber.Api.Media/SMTC/ISmtcSessionSource.cs`:

```csharp
using Windows.Media.Control;

namespace WinTabber.Api.Media.SMTC;

/// <summary>
/// The seam over <see cref="GlobalSystemMediaTransportControlsSessionManager.RequestAsync"/> — a
/// static WinRT factory that hits the real SMTC subsystem and cannot be substituted in unit tests
/// without this interface.
/// </summary>
public interface ISmtcSessionSource
{
    Task<GlobalSystemMediaTransportControlsSessionManager> RequestAsync();
}
```

- [ ] **Step 2: Create the fake**

`WinTabber.Api.Media.Tests/Fakes/FakeSmtcSessionSource.cs`:

```csharp
using Windows.Media.Control;
using WinTabber.Api.Media.SMTC;

namespace WinTabber.Api.Media.Tests.Fakes;

public sealed class FakeSmtcSessionSource(
    Func<Task<GlobalSystemMediaTransportControlsSessionManager>> requestAsync
) : ISmtcSessionSource
{
    public Task<GlobalSystemMediaTransportControlsSessionManager> RequestAsync() => requestAsync();
}
```

- [ ] **Step 3: Write the failing test**

`WinTabber.Api.Media.Tests/SMTC/SMTCSessionRepositoryTests.cs`:

```csharp
using System.Reactive.Linq;
using Windows.Media.Control;
using WinTabber.Api.Media.SMTC.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.SMTC;

public class SMTCSessionRepositoryTests
{
    [Test]
    public async Task ActiveMediaSessionChanges_PropagatesSourceFailure_WhenSessionManagerUnavailable()
    {
        var expected = new InvalidOperationException("SMTC unavailable");
        var source = new FakeSmtcSessionSource(
            () => Task.FromException<GlobalSystemMediaTransportControlsSessionManager>(expected)
        );
        var repository = new SMTCSessionRepository(source);

        await Assert
            .That(async () => await repository.ActiveMediaSessionChanges.FirstAsync())
            .Throws<InvalidOperationException>();
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/SMTCSessionRepositoryTests/*"`
Expected: FAIL to build — `SMTCSessionRepository` has no constructor accepting `ISmtcSessionSource`
yet (its current constructor is the implicit parameterless one).

- [ ] **Step 5: Implement — inject the source into `SMTCSessionRepository`**

Modify `WinTabber.Api.Media/SMTC/Repositories/SMTCSessionRepository.cs`. Add a `using` for the new
namespace, give the partial class a primary constructor, and replace the static call:

```csharp
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using Windows.Media.Control;
using WinTabber.Api.Media.SMTC;

namespace WinTabber.Api.Media.SMTC.Repositories;

public partial class SMTCSessionRepository(ISmtcSessionSource sessionSource)
{
    [Lazy(IsPrivate = true)]
    private IObservable<GlobalSystemMediaTransportControlsSessionManager> GetSessionManagerObservable()
    {
        return Observable
            .StartAsync(async () => await sessionSource.RequestAsync())
            .Replay(1)
            .RefCount();
    }

    // ... GetMediaSessionsChanges, GetActiveMediaSessionChanges, GetMediaSessions,
    // ToSessionChangeSet, GetSMTCActiveSessionChanges, GetSMTCSessionChanges all unchanged.
}
```

(Only the `GetSessionManagerObservable` body and the class declaration line change — every other
member in the file is untouched.)

- [ ] **Step 6: Create the production implementation**

`WinTabber.Api.Media/SMTC/SmtcSessionSource.cs`:

```csharp
using Windows.Media.Control;

namespace WinTabber.Api.Media.SMTC;

public sealed class SmtcSessionSource : ISmtcSessionSource
{
    public async Task<GlobalSystemMediaTransportControlsSessionManager> RequestAsync() =>
        await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
}
```

- [ ] **Step 7: Wire up DI**

Modify `WinTabberUI/Bootstrapper.cs`. Add a using alongside the existing SMTC one (line 8):

```csharp
using WinTabber.Api.Media.SMTC;
using WinTabber.Api.Media.SMTC.Repositories;
```

Add the registration immediately before the existing `SMTCSessionRepository` line (currently line
105, `.AddSingleton<SMTCSessionRepository>()` — leave that line as-is, DI already resolves its
constructor's parameters from the container):

```csharp
            .AddSingleton<ISmtcSessionSource, SmtcSessionSource>()
            .AddSingleton<SMTCSessionRepository>()
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/SMTCSessionRepositoryTests/*"`
Expected: PASS

- [ ] **Step 9: Run the full build and test suite**

Run: `dotnet build WinTabber.slnx` — expect 0 warnings, 0 errors.
Run: `dotnet test --solution WinTabber.slnx` — expect all tests green (no regressions in
`WinTabberUI`/`WinTabber.UI.Media` consumers of `SMTCSessionRepository`, which only ever resolve it
through DI).

- [ ] **Step 10: Commit**

```bash
git add WinTabber.Api.Media/SMTC/ISmtcSessionSource.cs WinTabber.Api.Media/SMTC/SmtcSessionSource.cs WinTabber.Api.Media/SMTC/Repositories/SMTCSessionRepository.cs WinTabber.Api.Media.Tests/Fakes/FakeSmtcSessionSource.cs WinTabber.Api.Media.Tests/SMTC/SMTCSessionRepositoryTests.cs WinTabberUI/Bootstrapper.cs
git commit -m "feat: extract ISmtcSessionSource seam for SMTCSessionRepository"
```

---

## Task 2: `IShellApplicationSource` — inject into `InstalledApplicationRepository`

**Files:**
- Create: `WinTabber.Api.Media/ShellApplications/IShellApplicationSource.cs`
- Create: `WinTabber.Api.Media/ShellApplications/WindowsShellApplicationSource.cs`
- Create: `WinTabber.Api.Media.Tests/Fakes/FakeShellApplicationSource.cs`
- Create: `WinTabber.Api.Media.Tests/ShellApplications/InstalledApplicationRepositoryTests.cs`
- Modify: `WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs`
- Modify: `WinTabberUI/Bootstrapper.cs:1-8` (usings), `:109` (registration)

**Interfaces:**
- Produces: `IShellApplicationSource.GetAppsFolder() : IKnownFolder`,
  `IShellApplicationSource.CreateShellItemImageFactory(string path) : IShellItemImageFactory` — the
  seam `InstalledApplicationRepository`'s constructor now takes, and what
  `FakeShellApplicationSource` implements for tests.

- [ ] **Step 1: Create the interface** (needed for the test below to compile)

`WinTabber.Api.Media/ShellApplications/IShellApplicationSource.cs`:

```csharp
using Microsoft.WindowsAPICodePack.Shell;
using Windows.Win32.UI.Shell;

namespace WinTabber.Api.Media.ShellApplications;

/// <summary>
/// The seam over the static Windows Shell APIs <see cref="InstalledApplicationRepository"/> used
/// to call directly — <c>KnownFolderHelper.FromKnownFolderId</c> and
/// <c>PInvoke.SHCreateItemFromParsingName</c> — both of which hit the real Windows shell and
/// cannot be substituted in unit tests without this interface.
/// </summary>
public interface IShellApplicationSource
{
    IKnownFolder GetAppsFolder();
    IShellItemImageFactory CreateShellItemImageFactory(string path);
}
```

- [ ] **Step 2: Create the fake**

`WinTabber.Api.Media.Tests/Fakes/FakeShellApplicationSource.cs`:

```csharp
using Microsoft.WindowsAPICodePack.Shell;
using Windows.Win32.UI.Shell;
using WinTabber.Api.Media.ShellApplications;

namespace WinTabber.Api.Media.Tests.Fakes;

public sealed class FakeShellApplicationSource(Func<IKnownFolder> getAppsFolder) : IShellApplicationSource
{
    public IKnownFolder GetAppsFolder() => getAppsFolder();

    public IShellItemImageFactory CreateShellItemImageFactory(string path) =>
        throw new NotSupportedException(
            "Not exercised by these tests — ShellObject/IShellItemImageFactory have no accessible "
                + "test constructor; see the Global Constraints note in this plan."
        );
}
```

- [ ] **Step 3: Write the failing test**

`WinTabber.Api.Media.Tests/ShellApplications/InstalledApplicationRepositoryTests.cs`:

```csharp
using System.Reactive.Linq;
using DynamicData;
using WinTabber.Api.Media.ShellApplications.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.ShellApplications;

public class InstalledApplicationRepositoryTests
{
    [Test]
    public async Task ApplicationsByAumid_PropagatesSourceFailure_WhenAppsFolderUnavailable()
    {
        var expected = new InvalidOperationException("Shell unavailable");
        var source = new FakeShellApplicationSource(() => throw expected);
        using var repository = new InstalledApplicationRepository(source);

        var tcs = new TaskCompletionSource<Exception>();
        using var subscription = repository.ApplicationsByAumid.Connect()
            .Subscribe(_ => { }, ex => tcs.TrySetResult(ex), () => { });

        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(error).IsEqualTo(expected);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/InstalledApplicationRepositoryTests/*"`
Expected: FAIL to build — `InstalledApplicationRepository`'s constructor is currently
parameterless.

- [ ] **Step 5: Implement — inject the source into `InstalledApplicationRepository`**

Modify `WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs`:

Remove the two now-unused static fields (moved to the new source class):

```csharp
    private static readonly Guid FOLDERID_AppsFolder = new Guid(
        "{1e87508d-89c2-42f0-8a7e-645a0f50ca58}"
    );
    private static readonly Guid GUID_IShellItem = typeof(IShellItem).GUID;
```

Add the injected field and update the constructor:

```csharp
    private readonly IShellApplicationSource _shellSource;
    private readonly SourceCache<InstalledApplicationInfo, string> _apps = new(static app =>
        app.AppUserModelId
    );
    private readonly Subject<Unit> _refreshSubject = new Subject<Unit>();

    public void Refresh()
    {
        _refreshSubject.OnNext(Unit.Default);
    }

    public InstalledApplicationRepository(IShellApplicationSource shellSource)
    {
        _shellSource = shellSource;
        var primaryAumidCache = GetInstalledApplicationsObservable()
            .ToObservableChangeSet(
                keySelector: app => app.AppUserModelId,
                expireAfter: item => TimeSpan.FromDays(1)
            );
        // ...rest of the constructor body is unchanged...
```

Remove `static` from the five methods below (they now reach the shell through `_shellSource`), and
update `GetAppImageFolder`'s body and `GetIcon`'s COM-creation block:

```csharp
    private IObservable<IReadOnlyList<InstalledApplicationInfo>> GetInstalledApplicationsObservable()
    {
        return Observable.Start<IReadOnlyList<InstalledApplicationInfo>>(
            () =>
            {
                Debug.WriteLine(
                    $"Fetching installed applications on thread {Environment.CurrentManagedThreadId}"
                );
                return GetInstalledApplicationsBlocking().ToArray();
            },
            TaskPoolScheduler.Default
        );
    }

    private IKnownFolder GetAppImageFolder() => _shellSource.GetAppsFolder();

    private IEnumerable<InstalledApplicationInfo> GetInstalledApplicationsBlocking()
    {
        Stopwatch sw = Stopwatch.StartNew();
        using var folder = GetAppImageFolder();
        foreach (var item in folder)
        {
            using (item)
            {
                if (IsValid(item))
                {
                    yield return CreateItem(item);
                }
            }
        }
        sw.Stop();
        Debug.WriteLine(
            $"Loaded installed applications in {sw.ElapsedMilliseconds} ms on thread {Thread.CurrentThread.ManagedThreadId}"
        );
    }
```

(`IsValid` and `GetAumid` stay `static` — neither needs `_shellSource`.)

```csharp
    private InstalledApplicationInfo CreateItem(ShellObject shellObject)
    {
        string? targetParsingPath = shellObject.Properties.System.Link.TargetParsingPath.Value;
        string? packageInstallPath = shellObject
            .Properties.GetProperty<string>(PackageInstallPath)
            .Value;
        string? path = packageInstallPath ?? targetParsingPath;
        return new InstalledApplicationInfo
        {
            AppUserModelId = GetAumid(shellObject),
            Icon = GetIcon(shellObject, path),
            Name = shellObject.Name,
            TargetPath = targetParsingPath,
            PackageInstallPath = packageInstallPath,
        };
    }
```

`GetIcon` replaces its manual `PInvoke.SHCreateItemFromParsingName` + cast block with a call to
`_shellSource.CreateShellItemImageFactory`, and releases the COM object it gets back instead of the
raw native pointer (same underlying RCW, same effect):

```csharp
    private IObservable<ImageSource> GetIcon(ShellObject shellObject, string path)
    {
        int width = (int)shellObject.Thumbnail.CurrentSize.Width;
        int height = (int)shellObject.Thumbnail.CurrentSize.Height;
        ThumbnailOptions options = ThumbnailOptions.None;
        return Observable
            .Defer(() =>
                Observable.Start(
                    () =>
                    {
                        unsafe
                        {
                            var imageFactory = _shellSource.CreateShellItemImageFactory(path);

                            SIZE size = new SIZE { cx = width, cy = height };

                            HBITMAP hBitmap = default;
                            try
                            {
                                try
                                {
                                    imageFactory.GetImage(size, (SIIGBF)options, &hBitmap);
                                }
                                catch (COMException ex)
                                    when (options == ThumbnailOptions.ThumbnailOnly
                                        && (
                                            ex.HResult == S_PATHNOTFOUND
                                            || ex.HResult == S_EXTRACTIONFAILED
                                        )
                                    )
                                {
                                    imageFactory.GetImage(
                                        size,
                                        (SIIGBF)ThumbnailOptions.IconOnly,
                                        &hBitmap
                                    );
                                }
                                catch (FileNotFoundException)
                                    when (options == ThumbnailOptions.ThumbnailOnly)
                                {
                                    imageFactory.GetImage(
                                        size,
                                        (SIIGBF)ThumbnailOptions.IconOnly,
                                        &hBitmap
                                    );
                                }
                                catch (System.Exception ex)
                                {
                                    throw new InvalidOperationException(
                                        "Failed to get thumbnail",
                                        ex
                                    );
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(imageFactory);
                            }

                            var image = Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                0,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions()
                            );
                            if (!image.IsFrozen && image.CanFreeze)
                            {
                                image.Freeze();
                            }

                            return image;
                        }
                    },
                    Scheduler.CurrentThread
                )
            )
            .Replay(1)
            .AutoConnect();
    }
```

Add `using WinTabber.Api.Media.ShellApplications;` to the file's using list. Remove
`using Windows.Win32;` — it was only there for the now-relocated `PInvoke.SHCreateItemFromParsingName`
call, and nothing else in this file uses the `PInvoke` class. Every other existing using —
`Windows.Win32.UI.Shell`, `Windows.Win32.Foundation`, `Windows.Win32.Graphics.Gdi`,
`Microsoft.WindowsAPICodePack.Shell`, etc. — stays, since `IShellItemImageFactory`, `SIZE`,
`HBITMAP`, `SIIGBF`, `S_PATHNOTFOUND`/`S_EXTRACTIONFAILED`, `ShellObject`, and `IKnownFolder` are
all still referenced directly in this file.

- [ ] **Step 6: Create the production implementation**

`WinTabber.Api.Media/ShellApplications/WindowsShellApplicationSource.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.WindowsAPICodePack.Shell;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace WinTabber.Api.Media.ShellApplications;

public sealed class WindowsShellApplicationSource : IShellApplicationSource
{
    private static readonly Guid FOLDERID_AppsFolder = new Guid(
        "{1e87508d-89c2-42f0-8a7e-645a0f50ca58}"
    );
    private static readonly Guid GUID_IShellItem = typeof(IShellItem).GUID;

    public IKnownFolder GetAppsFolder() => KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);

    public unsafe IShellItemImageFactory CreateShellItemImageFactory(string path)
    {
        PInvoke
            .SHCreateItemFromParsingName(path, null, GUID_IShellItem, out var nativeShellItem)
            .ThrowOnFailure();

        if (nativeShellItem is not IShellItemImageFactory imageFactory)
        {
            Marshal.ReleaseComObject(nativeShellItem);
            throw new InvalidOperationException("Failed to get IShellItemImageFactory");
        }

        return imageFactory;
    }
}
```

- [ ] **Step 7: Wire up DI**

Modify `WinTabberUI/Bootstrapper.cs`. Add a using alongside the existing ShellApplications one
(line 7):

```csharp
using WinTabber.Api.Media.ShellApplications;
using WinTabber.Api.Media.ShellApplications.Repositories;
```

Add the registration immediately before the existing `IInstalledApplicationRepository` line
(currently line 109):

```csharp
            .AddSingleton<IShellApplicationSource, WindowsShellApplicationSource>()
            .AddSingleton<IInstalledApplicationRepository, InstalledApplicationRepository>();
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test WinTabber.Api.Media.Tests -- --treenode-filter "/*/*/InstalledApplicationRepositoryTests/*"`
Expected: PASS

- [ ] **Step 9: Run the full build and test suite**

Run: `dotnet build WinTabber.slnx` — expect 0 warnings, 0 errors.
Run: `dotnet test --solution WinTabber.slnx` — expect all tests green.

- [ ] **Step 10: Commit**

```bash
git add WinTabber.Api.Media/ShellApplications/IShellApplicationSource.cs WinTabber.Api.Media/ShellApplications/WindowsShellApplicationSource.cs WinTabber.Api.Media/ShellApplications/Repositories/InstalledApplicationRepository.cs WinTabber.Api.Media.Tests/Fakes/FakeShellApplicationSource.cs WinTabber.Api.Media.Tests/ShellApplications/InstalledApplicationRepositoryTests.cs WinTabberUI/Bootstrapper.cs
git commit -m "feat: extract IShellApplicationSource seam for InstalledApplicationRepository"
```

---

## Task 3: Update documentation to match

**Files:**
- Modify: `WinTabber.Api.Media.Tests/README.md`
- Modify: `.todos/testability-review.md`

**Interfaces:** None — documentation only, no code.

- [ ] **Step 1: Update the test project README's "Not covered" section**

In `WinTabber.Api.Media.Tests/README.md`, replace this bullet:

```markdown
- SMTC (`SMTCSessionRepository`/`SMTCSessionMonitor`/`SMTCSessionService`) and ShellApplications
  (`InstalledApplicationRepository`)'s WinRT/COM glue — no existing seam. Not manufactured
  speculatively (YAGNI) — a future task that wants coverage here needs its own design pass.
```

with:

```markdown
- SMTC (`SMTCSessionMonitor`/`SMTCSessionService`) and ShellApplications' actual session/shell-item
  *processing* logic — `GlobalSystemMediaTransportControlsSession` and `ShellObject` are WinRT/COM
  types with no accessible test constructor, so `ISmtcSessionSource`/`IShellApplicationSource`
  (see `Fakes/FakeSmtcSessionSource.cs`, `Fakes/FakeShellApplicationSource.cs`) only make the
  *acquisition* step substitutable — covered by `SMTC/SMTCSessionRepositoryTests.cs` and
  `ShellApplications/InstalledApplicationRepositoryTests.cs`, both exercising only the
  acquisition-failure path. Full session/shell-item processing coverage would need its own design
  pass, the same way `IAudioDevice` was needed for `CoreAudioDeviceRepository`.
```

And add a new bullet under "Covered:" (above the "**Not covered, and why:**" heading):

```markdown
- `SMTCSessionRepository`'s and `InstalledApplicationRepository`'s acquisition-failure propagation
  — via `Fakes/FakeSmtcSessionSource.cs` and `Fakes/FakeShellApplicationSource.cs`.
```

- [ ] **Step 2: Mark items 9 and 10 done in `.todos/testability-review.md`**

Replace:

```markdown
### 9. Extract `IShellApplicationSource` for `InstalledApplicationRepository`
`KnownFolderHelper.FromKnownFolderId()`, `PInvoke.SHCreateItemFromParsingName()`, and related static Shell API calls are untestable without a real Windows shell.

- [ ] Define `IShellApplicationSource` interface
- [ ] Implement `WindowsShellApplicationSource` wrapping the current static calls
- [ ] Inject into `InstalledApplicationRepository`
```

with:

```markdown
### 9. ~~Extract `IShellApplicationSource` for `InstalledApplicationRepository`~~ — DONE
`KnownFolderHelper.FromKnownFolderId()`, `PInvoke.SHCreateItemFromParsingName()`, and related static Shell API calls are untestable without a real Windows shell.

> **Resolved 2026-09-11.** See `docs/superpowers/plans/2026-09-11-shell-smtc-source-abstraction.md`.
> `ShellObject` itself still has no accessible test constructor, so this makes the *acquisition*
> step substitutable, not full shell-item processing — see
> `WinTabber.Api.Media.Tests/README.md` for what that does and doesn't unlock.

- [x] Define `IShellApplicationSource` interface
- [x] Implement `WindowsShellApplicationSource` wrapping the current static calls
- [x] Inject into `InstalledApplicationRepository`
```

Replace:

```markdown
### 10. Extract `ISmtcSessionSource` for `SMTCSessionRepository`
`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` requires Windows 10 SMTC subsystem.

- [ ] Define `ISmtcSessionSource` interface
- [ ] Inject into `SMTCSessionRepository`
```

with:

```markdown
### 10. ~~Extract `ISmtcSessionSource` for `SMTCSessionRepository`~~ — DONE
`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` requires Windows 10 SMTC subsystem.

> **Resolved 2026-09-11.** See `docs/superpowers/plans/2026-09-11-shell-smtc-source-abstraction.md`.
> Same caveat as item 9: makes acquisition substitutable, not full session processing.

- [x] Define `ISmtcSessionSource` interface
- [x] Inject into `SMTCSessionRepository`
```

- [ ] **Step 3: Commit**

```bash
git add WinTabber.Api.Media.Tests/README.md .todos/testability-review.md
git commit -m "docs: update testability review and test README for the new source seams"
```
