# Phase 5B: Api.Media & Interop Test Coverage — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add narrowly-scoped, real test coverage to two currently-untested projects
(`WinTabber.Api.Media`, `WinTabber.Interop`), covering exactly what has a genuine seam and
documenting everything that doesn't, rather than padding either project with vacuous tests.

**Architecture:** Two new TUnit test projects, mirroring `WinTabber.Api.Windowing.Tests`'s shape
(a `Fakes/` folder where a fake is needed, direct `ProjectReference` to the project under test,
no `GlobalSetup.cs`/retry policy — that's only needed by WPF-coupled projects per T2.6).

**Tech Stack:** C# / .NET 10, TUnit, NAudio.

**Spec:** `docs/superpowers/specs/2026-09-04-phase-5-design.md` (T5.3 and T5.4 sections — T5.3's
scope was corrected during planning; read the correction before starting Task 1).

## Global Constraints

- Every task must end with `dotnet build WinTabber.slnx` at 0 warnings, 0 errors, and
  `dotnet test --solution WinTabber.slnx` fully green.
- Do not write a test that cannot fail for a real reason (mock-verifies-the-mock). If a member
  has no seam, document it as untested — do not stub around the limitation.
- Commit after each task: `chore: Phase 5B T<n> — <summary>`.
- This plan is independent of Phase 5A (`docs/superpowers/plans/2026-09-04-phase-5a-interop-split.md`)
  — it can be executed before, after, or interleaved with it.

---

### Task 1: Scaffold `WinTabber.Api.Media.Tests` and the enumerator fake

**Files:**
- Create: `WinTabber.Api.Media.Tests/WinTabber.Api.Media.Tests.csproj`
- Create: `WinTabber.Api.Media.Tests/Fakes/FakeMMDeviceEnumeratorWrapper.cs`
- Modify: `WinTabber.slnx`

**Interfaces:**
- Produces: `FakeMMDeviceEnumeratorWrapper : IMMDeviceEnumeratorWrapper` — the fake every later
  task in this plan's Api.Media half builds tests on.

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\WinTabber.Api.Media\WinTabber.Api.Media.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution**

In `WinTabber.slnx`, add (alphabetically, before `WinTabber.Api.Windowing.Tests`):

```xml
  <Project Path="WinTabber.Api.Media.Tests/WinTabber.Api.Media.Tests.csproj" />
```

- [ ] **Step 3: Write the fake**

```csharp
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinTabber.Api.Media.CoreAudio;

namespace WinTabber.Api.Media.Tests.Fakes;

/// <summary>
/// Hand-rolled fake for <see cref="IMMDeviceEnumeratorWrapper"/>. Only
/// <see cref="HasDefaultAudioEndpoint"/> and the endpoint-notification-callback methods are
/// implemented for real: NAudio's <see cref="MMDevice"/> has only an internal constructor taking
/// an internal COM interface, so no test code can construct one — the three device-returning
/// members (<see cref="GetDefaultAudioEndpoint"/>, <see cref="EnumerateAudioEndPoints"/>,
/// <see cref="GetDevice"/>) throw <see cref="NotSupportedException"/> so accidental use in a test
/// that would need a real device fails loudly instead of silently returning null/garbage.
/// </summary>
public sealed class FakeMMDeviceEnumeratorWrapper : IMMDeviceEnumeratorWrapper
{
    private readonly HashSet<(DataFlow Flow, Role Role)> _defaultEndpoints = [];
    public List<IMMNotificationClient> RegisteredCallbacks { get; } = [];
    public List<IMMNotificationClient> UnregisteredCallbacks { get; } = [];

    public void SetHasDefaultAudioEndpoint(DataFlow flow, Role role, bool value)
    {
        if (value)
            _defaultEndpoints.Add((flow, role));
        else
            _defaultEndpoints.Remove((flow, role));
    }

    public bool HasDefaultAudioEndpoint(DataFlow dataFlow, Role role) => _defaultEndpoints.Contains((dataFlow, role));

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) => RegisteredCallbacks.Add(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) => UnregisteredCallbacks.Add(client);

    public void Dispose() { }

    // ── Cannot be faked: MMDevice has no accessible constructor ────────────

    public MMDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role) =>
        throw new NotSupportedException("MMDevice cannot be constructed by test code (internal constructor).");

    public IEnumerable<MMDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState deviceState) =>
        throw new NotSupportedException("MMDevice cannot be constructed by test code (internal constructor).");

    public MMDevice GetDevice(string id) =>
        throw new NotSupportedException("MMDevice cannot be constructed by test code (internal constructor).");
}
```

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors — the fake compiles and satisfies the interface; no tests exist
yet to run.

- [ ] **Step 5: Commit**

```bash
git add WinTabber.Api.Media.Tests/ WinTabber.slnx
git commit -m "chore: Phase 5B T1 — scaffold WinTabber.Api.Media.Tests, add FakeMMDeviceEnumeratorWrapper"
```

---

### Task 2: Test `CoreAudioDeviceRepository`'s default-endpoint and callback-wiring logic

**Files:**
- Create: `WinTabber.Api.Media.Tests/CoreAudio/CoreAudioDeviceRepositoryTests.cs`

**Interfaces:**
- Consumes: `FakeMMDeviceEnumeratorWrapper` (Task 1), `CoreAudioDeviceRepository`
  (`WinTabber.Api.Media/CoreAudio/Repositories/CoreAudioDeviceRepository.cs`)

- [ ] **Step 1: Write the test file**

```csharp
using System.Reactive.Concurrency;
using NAudio.CoreAudioApi;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.CoreAudio;

public class CoreAudioDeviceRepositoryTests
{
    [Test]
    public async Task GetDefaultPlaybackDevice_ReturnsNull_WhenNoDefaultRenderEndpoint()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultPlaybackDevice();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDefaultRecordingDevice_ReturnsNull_WhenNoDefaultCaptureEndpoint()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultRecordingDevice();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Constructor_RegistersEndpointNotificationCallback()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();

        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        await Assert.That(enumerator.RegisteredCallbacks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Dispose_UnregistersEndpointNotificationCallback()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        repository.Dispose();

        await Assert.That(enumerator.UnregisteredCallbacks.Count).IsEqualTo(1);
    }
}
```

`GetDefaultPlaybackDevice()`/`GetDefaultRecordingDevice()` check `HasDefaultAudioEndpoint` first
and return `null` without calling `GetDefaultAudioEndpoint` when it's `false` — since
`FakeMMDeviceEnumeratorWrapper.SetHasDefaultAudioEndpoint` is never called in these two tests,
`HasDefaultAudioEndpoint` returns `false` by default, so the `GetDefaultAudioEndpoint` throw path
is never hit. This is the only branch of these two methods testable through this seam (see the
spec's T5.3 correction) — the `true` branch is left untested, tracked as T5.5.

- [ ] **Step 2: Run the new tests**

Run: `dotnet test WinTabber.Api.Media.Tests/WinTabber.Api.Media.Tests.csproj`
Expected: 4 passed, 0 failed.

- [ ] **Step 3: Run the full solution suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: previous baseline count + 4, all passing, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add WinTabber.Api.Media.Tests/CoreAudio/
git commit -m "chore: Phase 5B T2 — test CoreAudioDeviceRepository's default-endpoint null path and callback wiring"
```

---

### Task 3: Document what's out of scope

**Files:**
- Create: `WinTabber.Api.Media.Tests/README.md`

**Interfaces:** None (documentation only).

- [ ] **Step 1: Write the scope note**

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add WinTabber.Api.Media.Tests/README.md
git commit -m "chore: Phase 5B T3 — document WinTabber.Api.Media.Tests's deliberate scope"
```

---

### Task 4: Extract `ClassifyNonSystemProcesses` from `ProcessHelper.GetNonSystemProcesses`

**Files:**
- Modify: `WinTabber.Interop/ProcessHelper.cs`

**Interfaces:**
- Produces: `ProcessHelper.ClassifyNonSystemProcesses(IEnumerable<ProcessInfo>) : IEnumerable<ProcessInfo>`
  — the pure function Task 5's tests call directly.

- [ ] **Step 1: Split the method**

In `WinTabber.Interop/ProcessHelper.cs`, change:

```csharp
    public static IEnumerable<ProcessInfo> GetNonSystemProcesses()
    {
        Dictionary<int, bool> processMap = new();
        foreach (var process in GetProcesses())
        {
            var isSelfSystem =
                process.Id == 0
                || process.ParentId == 0
                || string.Equals(process.ProcessName, "svchost", StringComparison.OrdinalIgnoreCase);
            var isParentSystem = processMap.GetValueOrDefault(process.ParentId, false);
            var isSystem = isSelfSystem || isParentSystem;
            processMap.Add(process.Id, isSystem);
            if (!isSystem)
            {
                yield return process;
            }
        }
    }
```

to:

```csharp
    public static IEnumerable<ProcessInfo> GetNonSystemProcesses()
    {
        return ClassifyNonSystemProcesses(GetProcesses());
    }

    /// <summary>
    /// Filters out system processes and every descendant of a system process. A process counts as
    /// a system process if it's PID 0, has parent PID 0, is named "svchost", or its parent was
    /// already classified as a system process — so this depends on <paramref name="processes"/>
    /// yielding each process after its parent (true of <see cref="GetProcesses"/>'s toolhelp
    /// snapshot order, but not enforced here).
    /// </summary>
    public static IEnumerable<ProcessInfo> ClassifyNonSystemProcesses(IEnumerable<ProcessInfo> processes)
    {
        Dictionary<int, bool> processMap = new();
        foreach (var process in processes)
        {
            var isSelfSystem =
                process.Id == 0
                || process.ParentId == 0
                || string.Equals(process.ProcessName, "svchost", StringComparison.OrdinalIgnoreCase);
            var isParentSystem = processMap.GetValueOrDefault(process.ParentId, false);
            var isSystem = isSelfSystem || isParentSystem;
            processMap.Add(process.Id, isSystem);
            if (!isSystem)
            {
                yield return process;
            }
        }
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build WinTabber.slnx`
Expected: 0 warnings, 0 errors. Pure refactor — `GetNonSystemProcesses()`'s behavior is
unchanged (it now delegates to `ClassifyNonSystemProcesses(GetProcesses())` instead of inlining
the same loop).

- [ ] **Step 3: Commit**

```bash
git add WinTabber.Interop/ProcessHelper.cs
git commit -m "chore: Phase 5B T4 — extract ClassifyNonSystemProcesses as a pure, testable function"
```

---

### Task 5: Scaffold `WinTabber.Interop.Tests` and test the extracted logic

**Files:**
- Create: `WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj`
- Create: `WinTabber.Interop.Tests/ProcessHelperTests.cs`
- Create: `WinTabber.Interop.Tests/README.md`
- Modify: `WinTabber.slnx`

**Interfaces:**
- Consumes: `ProcessHelper.IsSystemProcess`, `ProcessHelper.ClassifyNonSystemProcesses` (Task 4),
  `ProcessInfo` (`WinTabber.Interop/ProcessInfo.cs`, `record struct ProcessInfo(int Id, string
  ProcessName, int ParentId)`)

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\WinTabber.Interop\WinTabber.Interop.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution**

In `WinTabber.slnx`, add (alphabetically, after `WinTabber.Infrastructure.Tests`, before
`WinTabber.Interop`):

```xml
  <Project Path="WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj" />
```

- [ ] **Step 3: Write the tests**

```csharp
using WinTabber.Interop;

namespace WinTabber.Interop.Tests;

public class ProcessHelperTests
{
    [Test]
    public async Task IsSystemProcess_ReturnsFalse_ForTheCurrentTestProcess()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();

        bool result = ProcessHelper.IsSystemProcess(current);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsSystemProcess_ByPid_ReturnsTrue_ForPidZero()
    {
        bool result = ProcessHelper.IsSystemProcess(0);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_ExcludesPidZero()
    {
        var processes = new[] { new ProcessInfo(0, "System Idle Process", 0) };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_ExcludesProcessesNamedSvchost_CaseInsensitive()
    {
        var processes = new[] { new ProcessInfo(100, "SvcHost", 4) };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_ExcludesChildOfSystemProcess()
    {
        var processes = new[]
        {
            new ProcessInfo(100, "svchost", 4), // system: named svchost
            new ProcessInfo(200, "child.exe", 100), // system: parent (100) is system
        };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_IncludesOrdinaryProcess()
    {
        var processes = new[] { new ProcessInfo(1234, "notepad", 999) };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Id).IsEqualTo(1234);
    }

    [Test]
    public async Task ClassifyNonSystemProcesses_IncludesOrdinaryChildOfOrdinaryParent()
    {
        var processes = new[]
        {
            new ProcessInfo(1000, "explorer.exe", 4),
            new ProcessInfo(1001, "notepad.exe", 1000),
        };

        var result = ProcessHelper.ClassifyNonSystemProcesses(processes).ToList();

        await Assert.That(result.Count).IsEqualTo(2);
    }
}
```

Note the fifth test's process has `ParentId = 4` and is named `"explorer.exe"`, not `"explorer"`
— `IsSystemProcess(Process)` matches the bare name `"explorer"` via `_knownSystemProcessNames`,
but `ClassifyNonSystemProcesses`'s own `isSelfSystem` check only special-cases `"svchost"` by
name (matching the actual source: `GetNonSystemProcesses`'s inline check never referenced
`_knownSystemProcessNames`, only `"svchost"` — this asymmetry between `IsSystemProcess` and
`ClassifyNonSystemProcesses` is existing behavior, not something this task changes; the test
above is written to match the code as it exists, not as it perhaps "should" be. Do not
harmonize the two checks as part of this task — that would be a behavior change beyond
"add test coverage," and is out of scope).

- [ ] **Step 4: Write the scope note**

```markdown
# WinTabber.Interop.Tests

Deliberately narrow. Covered:

- `ProcessHelper.IsSystemProcess` and `ProcessHelper.ClassifyNonSystemProcesses` — the only pure
  logic in this project not requiring a real Win32 call.

**Not covered, and why:**

- `InteropProxy`, `NativeMethods`, `NtNativeMethods`, `PInvoke`, `MediaKeySender`, `UacHelper`'s
  elevation check, `ChromeInterop` — all P/Invoke wrapper glue. A test here would only verify
  that the wrapper calls the Win32 function it wraps (mock-verifies-the-mock), the same argument
  T3.1 already made about `WinTabber.UI.Common/Chrome`'s `CloakHelper`/`PeekHelper`.
- `WindowPlacement`, `ProcessInfo` — plain data records, nothing to test.
- `ProcessHelper.GetParentProcess`, `GetProcesses`, `GetProcessesByName`,
  `TryGetProcessExecutablePath` — all directly call live Win32 APIs (`NtQueryInformationProcess`,
  `CreateToolhelp32Snapshot`, `OpenProcess`/`QueryFullProcessImageName`); no seam without
  introducing an abstraction over the OS process table, which is not justified by this task.

See `docs/superpowers/specs/2026-09-04-phase-5-design.md`'s T5.4 section for the full reasoning.
```

- [ ] **Step 5: Run the new tests**

Run: `dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj`
Expected: 7 passed, 0 failed.

- [ ] **Step 6: Run the full solution suite**

Run: `dotnet test --solution WinTabber.slnx`
Expected: previous baseline count + 7, all passing, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add WinTabber.Interop.Tests/ WinTabber.slnx
git commit -m "chore: Phase 5B T5 — scaffold WinTabber.Interop.Tests, cover IsSystemProcess and ClassifyNonSystemProcesses"
```

---

## Final verification

- [ ] `dotnet build WinTabber.slnx` — 0 warnings, 0 errors.
- [ ] `dotnet test --solution WinTabber.slnx` — baseline (81, or whatever Phase 5A left it at) +
  11 (4 from Task 2, 7 from Task 5), all passing, 0 failed, 0 skipped.
- [ ] Both new projects appear in `WinTabber.slnx` and build/test independently:
  `dotnet test WinTabber.Api.Media.Tests/WinTabber.Api.Media.Tests.csproj` and
  `dotnet test WinTabber.Interop.Tests/WinTabber.Interop.Tests.csproj`.
