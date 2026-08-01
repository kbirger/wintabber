using System.Security.Cryptography;
using System.Text;
using DynamicData;
using WinTabber.API.Suspension;
using WinTabber.Api.Tests.Fakes;

namespace WinTabber.Api.Tests.Suspension;

public class ProcessSuspensionServiceTests
{
    private const int Pid = 5000;
    private const string ImagePath = @"C:\Program Files\Notepad\notepad.exe";

    private static string HashPath(string path)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static (
        ProcessSuspensionService service,
        FakeInteropProxy interop,
        FakeProcessRepository processRepository,
        InMemorySuspendedWindowStore store,
        NtProcessSuspensionStrategy processStrategy,
        ThreadSuspensionStrategy threadStrategy
    ) CreateService(InMemorySuspendedWindowStore? store = null, Action<FakeInteropProxy>? configureInterop = null)
    {
        var interop = new FakeInteropProxy();
        configureInterop?.Invoke(interop);
        var processRepository = new FakeProcessRepository();
        store ??= new InMemorySuspendedWindowStore();
        var processStrategy = new NtProcessSuspensionStrategy(interop);
        var threadStrategy = new ThreadSuspensionStrategy(interop);

        // Configuration (e.g. image paths) must be applied BEFORE construction: the ctor
        // performs startup pruning, which resolves each persisted entry's image path.
        var service = new ProcessSuspensionService(interop, processRepository, store, [processStrategy, threadStrategy]);

        return (service, interop, processRepository, store, processStrategy, threadStrategy);
    }

    [Test]
    public async Task Suspend_HidesHandlesAndCallsStrategy_AddsEntryAndPersists()
    {
        var (service, interop, _, store, _, _) = CreateService();
        interop.ImagePaths[Pid] = ImagePath;

        bool result = service.Suspend(Pid, "notepad", "Untitled - Notepad", [10, 20, 30]);

        await Assert.That(result).IsTrue();
        await Assert.That(interop.HiddenHandles).Contains(10);
        await Assert.That(interop.HiddenHandles).Contains(20);
        await Assert.That(interop.HiddenHandles).Contains(30);
        await Assert.That(interop.SuspendedProcessPids).Contains(Pid);
        await Assert.That(service.IsSuspended(Pid)).IsTrue();
        await Assert.That(store.Load().Any(e => e.ProcessId == Pid)).IsTrue();
    }

    [Test]
    public async Task CanSuspend_RefusesElevatedProcess()
    {
        var (service, _, _, _, _, _) = CreateService();

        await Assert.That(service.CanSuspend(Pid, isProcessElevated: true)).IsFalse();
    }

    [Test]
    public async Task CanSuspend_RefusesOwnPid()
    {
        var (service, _, processRepository, _, _, _) = CreateService();
        processRepository.CurrentProcessId = Pid;

        await Assert.That(service.CanSuspend(Pid, isProcessElevated: false)).IsFalse();
    }

    [Test]
    public async Task CanSuspend_RefusesSystemPids()
    {
        var (service, _, _, _, _, _) = CreateService();

        await Assert.That(service.CanSuspend(0, isProcessElevated: false)).IsFalse();
        await Assert.That(service.CanSuspend(4, isProcessElevated: false)).IsFalse();
    }

    [Test]
    public async Task CanSuspend_RefusesAlreadySuspendedPid()
    {
        var (service, interop, _, _, _, _) = CreateService();
        interop.ImagePaths[Pid] = ImagePath;
        service.Suspend(Pid, "notepad", "Untitled - Notepad", [10]);

        await Assert.That(service.CanSuspend(Pid, isProcessElevated: false)).IsFalse();
    }

    [Test]
    public async Task Suspend_RestoresWindowsAndReturnsFalse_WhenStrategyThrows()
    {
        var (service, interop, _, store, _, _) = CreateService();
        interop.ImagePaths[Pid] = ImagePath;
        interop.ThrowOnSuspendProcess = new InvalidOperationException("boom");

        bool result = service.Suspend(Pid, "notepad", "Untitled - Notepad", [10, 20]);

        await Assert.That(result).IsFalse();
        await Assert.That(interop.HiddenHandles.Count).IsEqualTo(0);
        await Assert.That(interop.RestoredHandles).Contains(10);
        await Assert.That(interop.RestoredHandles).Contains(20);
        await Assert.That(service.IsSuspended(Pid)).IsFalse();
        await Assert.That(store.Load().Any(e => e.ProcessId == Pid)).IsFalse();
    }

    [Test]
    public async Task Suspend_AbortsBeforeHidingAnything_WhenImagePathThrows()
    {
        var (service, interop, _, store, _, _) = CreateService();
        // No image path configured for Pid -> GetProcessImagePath throws.

        bool result = service.Suspend(Pid, "notepad", "Untitled - Notepad", [10, 20]);

        await Assert.That(result).IsFalse();
        await Assert.That(interop.HiddenHandles.Count).IsEqualTo(0);
        await Assert.That(interop.SuspendedProcessPids.Count).IsEqualTo(0);
        await Assert.That(service.IsSuspended(Pid)).IsFalse();
        await Assert.That(store.Load().Any(e => e.ProcessId == Pid)).IsFalse();
    }

    [Test]
    public async Task Resume_RestoresHandlesAndUsesRecordedStrategy_RemovesEntry()
    {
        var store = new InMemorySuspendedWindowStore();
        string hash = HashPath(ImagePath);
        // Entry was suspended with the "thread" strategy, not the default ("process").
        store.Seed(new SuspendedWindowEntry(Pid, [10, 20], hash, "notepad", "Untitled - Notepad", "thread"));

        var (service, interop, _, _, _, _) = CreateService(store, i => i.ImagePaths[Pid] = ImagePath);

        bool result = service.Resume(Pid);

        await Assert.That(result).IsTrue();
        await Assert.That(interop.RestoredHandles).Contains(10);
        await Assert.That(interop.RestoredHandles).Contains(20);
        await Assert.That(interop.ResumedThreadPids).Contains(Pid);
        await Assert.That(interop.ResumedProcessPids.Count).IsEqualTo(0);
        await Assert.That(service.IsSuspended(Pid)).IsFalse();
    }

    [Test]
    public async Task Resume_MismatchedPathHash_DoesNotResumeOrRestore_ButRemovesEntry()
    {
        var store = new InMemorySuspendedWindowStore();
        string staleHash = HashPath(ImagePath);
        store.Seed(new SuspendedWindowEntry(Pid, [10, 20], staleHash, "notepad", "Untitled - Notepad", "process"));

        // At startup the PID still resolves to the original path, so pruning keeps the entry.
        var (service, interop, _, _, _, _) = CreateService(store, i => i.ImagePaths[Pid] = ImagePath);

        // Between startup and the resume click, the PID was reused by a different process.
        interop.ImagePaths[Pid] = @"C:\Windows\System32\calc.exe";

        bool result = service.Resume(Pid);

        await Assert.That(result).IsFalse();
        await Assert.That(interop.RestoredHandles.Count).IsEqualTo(0);
        await Assert.That(interop.ResumedProcessPids.Count).IsEqualTo(0);
        await Assert.That(interop.ResumedThreadPids.Count).IsEqualTo(0);
        await Assert.That(service.IsSuspended(Pid)).IsFalse();
    }

    [Test]
    public async Task ResumeAll_ResumesEverythingAndDeletesStore()
    {
        var (service, interop, _, store, _, _) = CreateService();
        interop.ImagePaths[100] = @"C:\a.exe";
        interop.ImagePaths[200] = @"C:\b.exe";
        service.Suspend(100, "a", "A", [1]);
        service.Suspend(200, "b", "B", [2]);

        service.ResumeAll();

        await Assert.That(interop.RestoredHandles).Contains(1);
        await Assert.That(interop.RestoredHandles).Contains(2);
        await Assert.That(service.IsSuspended(100)).IsFalse();
        await Assert.That(service.IsSuspended(200)).IsFalse();
        await Assert.That(store.Deleted).IsTrue();
    }

    [Test]
    public async Task StartupPruning_DropsStaleEntries_AndPersistsPrunedSet()
    {
        var store = new InMemorySuspendedWindowStore();
        string liveHash = HashPath(ImagePath);
        string wrongHash = HashPath(@"C:\other\different.exe");

        var live = new SuspendedWindowEntry(1, [1], liveHash, "live", "Live", "process");
        var hashMismatch = new SuspendedWindowEntry(2, [2], wrongHash, "stale", "Stale", "process");
        var gone = new SuspendedWindowEntry(3, [3], liveHash, "gone", "Gone", "process");
        store.Seed(live, hashMismatch, gone);

        var interop = new FakeInteropProxy();
        interop.ImagePaths[1] = ImagePath; // matches liveHash -> kept
        interop.ImagePaths[2] = ImagePath; // hash differs from stored wrongHash -> dropped
        // pid 3 has no configured image path -> GetProcessImagePath throws -> dropped

        var processRepository = new FakeProcessRepository();
        var service = new ProcessSuspensionService(
            interop,
            processRepository,
            store,
            [new NtProcessSuspensionStrategy(interop), new ThreadSuspensionStrategy(interop)]
        );

        await Assert.That(service.IsSuspended(1)).IsTrue();
        await Assert.That(service.IsSuspended(2)).IsFalse();
        await Assert.That(service.IsSuspended(3)).IsFalse();

        var persisted = store.Load();
        await Assert.That(persisted.Count).IsEqualTo(1);
        await Assert.That(persisted[0].ProcessId).IsEqualTo(1);
    }

    [Test]
    public async Task HasSuspendedChanges_TracksSuspendAndResumeLifecycle()
    {
        var (service, interop, _, _, _, _) = CreateService();
        interop.ImagePaths[Pid] = ImagePath;

        var emissions = new List<bool>();
        using var subscription = service.HasSuspendedChanges.Subscribe(emissions.Add);

        await Assert.That(emissions.Count).IsEqualTo(1);
        await Assert.That(emissions[0]).IsFalse();

        service.Suspend(Pid, "notepad", "Untitled - Notepad", [10]);

        await Assert.That(emissions[^1]).IsTrue();

        service.Resume(Pid);

        await Assert.That(emissions[^1]).IsFalse();
    }

    [Test]
    public async Task Connect_EmitsAddOnSuspend_AndRemoveOnResume()
    {
        var (service, interop, _, _, _, _) = CreateService();
        interop.ImagePaths[Pid] = ImagePath;

        var changes = new List<ChangeReason>();
        using var subscription = service.Connect().Subscribe(changeSet =>
        {
            foreach (var change in changeSet)
            {
                changes.Add(change.Reason);
            }
        });

        service.Suspend(Pid, "notepad", "Untitled - Notepad", [10]);
        service.Resume(Pid);

        await Assert.That(changes).Contains(ChangeReason.Add);
        await Assert.That(changes).Contains(ChangeReason.Remove);
    }

    [Test]
    public async Task HasSuspendedChanges_LateSubscriberSeesCurrentValue_NotStaleSeed()
    {
        var (service, interop, _, _, _, _) = CreateService();
        interop.ImagePaths[Pid] = ImagePath;

        // Establish + drop an initial subscription while nothing is suspended, to prove the
        // late subscriber below isn't just replaying a value cached at construction time.
        using (service.HasSuspendedChanges.Subscribe(_ => { })) { }

        service.Suspend(Pid, "notepad", "Untitled - Notepad", [10]);

        bool? lateValue = null;
        using var lateSubscription = service.HasSuspendedChanges.Subscribe(v => lateValue = v);

        await Assert.That(lateValue).IsEqualTo(true);
    }
}
