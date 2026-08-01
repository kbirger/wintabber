using System.Diagnostics;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using DynamicData;
using WinTabber.Interop;

namespace WinTabber.API.Suspension;

/// <summary>
/// Tracks which processes are suspended and provides suspend / resume operations.
/// Supports swappable <see cref="ISuspensionStrategy"/> implementations at runtime.
/// </summary>
public sealed class ProcessSuspensionService : IProcessSuspensionService
{
    private readonly IInteropProxy _interop;
    private readonly IProcessRepository _processRepository;
    private readonly ISuspendedWindowStore _store;
    private readonly IReadOnlyList<ISuspensionStrategy> _strategies;
    private readonly ISuspensionStrategy _defaultStrategy;
    private readonly SourceCache<SuspendedWindowEntry, int> _cache = new(e => e.ProcessId);

    public ProcessSuspensionService(
        IInteropProxy interop,
        IProcessRepository processRepository,
        ISuspendedWindowStore store,
        IEnumerable<ISuspensionStrategy> strategies
    )
    {
        _interop = interop;
        _processRepository = processRepository;
        _store = store;
        _strategies = strategies as IReadOnlyList<ISuspensionStrategy> ?? strategies.ToList();
        _defaultStrategy = _strategies[0];

        // Startup pruning: drop entries whose PID no longer resolves or whose image-path hash
        // no longer matches (PID reused by an unrelated process).
        var pruned = new List<SuspendedWindowEntry>();
        foreach (var entry in store.Load())
        {
            if (TryGetCurrentPathHash(entry.ProcessId, out string? hash) && string.Equals(hash, entry.PathHash, StringComparison.OrdinalIgnoreCase))
            {
                pruned.Add(entry);
            }
        }

        if (pruned.Count > 0)
        {
            _cache.AddOrUpdate(pruned);
        }
        Persist();

        // Deferred so a late subscriber (or a resubscribe after the RefCount drops to zero)
        // seeds from the live count rather than replaying the value captured at construction.
        HasSuspendedChanges = Observable
            .Defer(() => _cache.CountChanged.Select(c => c > 0).StartWith(_cache.Count > 0))
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
    }

    public IObservable<bool> HasSuspendedChanges { get; }

    public IObservable<IChangeSet<SuspendedWindowEntry, int>> Connect() => _cache.Connect();

    public bool IsSuspended(int pid) => _cache.Lookup(pid).HasValue;

    public bool CanSuspend(int pid, bool isProcessElevated)
    {
        if (isProcessElevated)
            return false;
        if (pid == _processRepository.GetCurrentProcessId())
            return false;
        if (pid <= 4)
            return false;
        if (IsSuspended(pid))
            return false;
        return true;
    }

    public bool CanSuspend(WindowRef window) => CanSuspend(window.Process.ProcessInstance.Id, window.Process.IsProcessElevated);

    public bool Suspend(WindowRef window)
    {
        if (!CanSuspend(window))
            return false;

        int pid = window.Process.ProcessInstance.Id;
        int[] handles = window.Process.GetWindows().Select(w => w.Handle).ToArray();
        string processName = window.Process.ProcessInstance.ProcessName;
        string title = window.Title;

        return Suspend(pid, processName, title, handles);
    }

    public bool Suspend(int pid, string processName, string title, IReadOnlyList<int> windowHandles)
    {
        try
        {
            if (!CanSuspend(pid, isProcessElevated: false))
                return false;

            // Hash the image path FIRST so a failure aborts before anything is hidden.
            string path;
            try
            {
                path = _interop.GetProcessImagePath(pid);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessSuspensionService: could not resolve image path for pid {pid}: {ex}");
                return false;
            }
            string pathHash = HashPath(path);

            foreach (int handle in windowHandles)
            {
                _interop.HideWindow(handle);
            }

            try
            {
                _defaultStrategy.Suspend(pid);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessSuspensionService: suspend failed for pid {pid}: {ex}");
                foreach (int handle in windowHandles)
                {
                    _interop.RestoreWindow(handle);
                }
                return false;
            }

            var entry = new SuspendedWindowEntry(pid, windowHandles.ToArray(), pathHash, processName, title, _defaultStrategy.Name);
            _cache.AddOrUpdate(entry);
            Persist();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProcessSuspensionService: unexpected error suspending pid {pid}: {ex}");
            return false;
        }
    }

    public bool Resume(int pid)
    {
        try
        {
            var lookup = _cache.Lookup(pid);
            if (!lookup.HasValue)
                return false;

            SuspendedWindowEntry entry = lookup.Value;

            // Remove the entry either way so the user is never stuck with an unresumable row.
            _cache.Remove(pid);
            Persist();

            if (!TryGetCurrentPathHash(pid, out string? currentHash) || !string.Equals(currentHash, entry.PathHash, StringComparison.OrdinalIgnoreCase))
            {
                // Process is gone, or the PID was reused by a different process.
                return false;
            }

            ISuspensionStrategy strategy = _strategies.FirstOrDefault(s => s.Name == entry.StrategyName) ?? _defaultStrategy;
            strategy.Resume(pid);

            foreach (int handle in entry.WindowHandles)
            {
                _interop.RestoreWindow(handle);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProcessSuspensionService: unexpected error resuming pid {pid}: {ex}");
            return false;
        }
    }

    public void ResumeAll()
    {
        foreach (var entry in _cache.Items.ToList())
        {
            try
            {
                Resume(entry.ProcessId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessSuspensionService: ResumeAll failed for pid {entry.ProcessId}: {ex}");
            }
        }

        _store.Delete();
    }

    public void Dispose()
    {
        ResumeAll();
        _cache.Dispose();
    }

    /// <summary>
    /// Persistence is best-effort: losing the file costs crash-recovery, but throwing here would
    /// unwind a suspend that has already frozen the process.
    /// </summary>
    private void Persist()
    {
        try
        {
            _store.Save(_cache.Items.ToList());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProcessSuspensionService: failed to persist suspension state: {ex}");
        }
    }

    private bool TryGetCurrentPathHash(int pid, out string? hash)
    {
        try
        {
            hash = HashPath(_interop.GetProcessImagePath(pid));
            return true;
        }
        catch
        {
            hash = null;
            return false;
        }
    }

    private static string HashPath(string path)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
