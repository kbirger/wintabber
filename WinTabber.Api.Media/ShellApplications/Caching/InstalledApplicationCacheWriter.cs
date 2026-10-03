using System.Diagnostics;
using System.Threading.Channels;

namespace WinTabber.Api.Media.ShellApplications.Caching;

/// <summary>
/// The only code that writes the on-disk installed-application cache. Every write is a message on
/// one bounded channel, read by one consumer task, so two saves can never open the same temp file
/// and no lock is needed.
/// The consumer batches. A burst of icons that finish extracting close together costs one save of
/// the icon blob, not one save per icon.
/// </summary>
internal sealed class InstalledApplicationCacheWriter : IDisposable
{
    // Small on purpose. A full channel drops an icon (see TryQueueIcon), and a dropped icon is safe:
    // the next request for that app extracts it again. It never blocks an extraction thread.
    private const int Capacity = 64;

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private abstract record Message;

    private sealed record IconReady(string AppUserModelId, byte[] PngBytes) : Message;

    private sealed record MetadataReady(IReadOnlyList<CachedApplicationEntry> Entries) : Message;

    private sealed record FlushRequested(TaskCompletionSource Completion) : Message;

    private readonly IInstalledApplicationCacheStore _store;
    private readonly TimeSpan _batchWindow;
    private readonly Channel<Message> _channel = Channel.CreateBounded<Message>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }
    );
    private readonly SemaphoreSlim _flushNow = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _consumer;

    // Touched only by the consumer task.
    private readonly Dictionary<string, byte[]> _pendingIcons = new(StringComparer.Ordinal);
    private Dictionary<string, CachedApplicationEntry> _current;
    private IReadOnlyList<CachedApplicationEntry>? _pendingEntries;

    /// <param name="initialEntries">The entries as they are on disk now, with their icon offsets.</param>
    /// <param name="batchWindow">How long the consumer waits for more messages before it saves.</param>
    public InstalledApplicationCacheWriter(
        IInstalledApplicationCacheStore store,
        IReadOnlyList<CachedApplicationEntry> initialEntries,
        TimeSpan batchWindow
    )
    {
        _store = store;
        _batchWindow = batchWindow;
        _current = ToDictionary(initialEntries);
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>
    /// Queues one extracted icon. Never blocks.
    /// </summary>
    /// <returns><see langword="false"/> if the writer is disposed or the channel is full. The icon is dropped.</returns>
    public bool TryQueueIcon(string appUserModelId, byte[] pngBytes) =>
        pngBytes.Length > 0 && _channel.Writer.TryWrite(new IconReady(appUserModelId, pngBytes));

    /// <summary>
    /// Queues the app list from a completed Shell scan. Blocks while the channel is full, so it must
    /// not run on a UI thread. A metadata update must not be dropped: no later scan would repeat it.
    /// </summary>
    public void QueueMetadata(IReadOnlyList<CachedApplicationEntry> entries) =>
        _channel.Writer.WriteAsync(new MetadataReady(entries)).AsTask().GetAwaiter().GetResult();

    /// <summary>Completes after every message queued before this call is on disk.</summary>
    public async Task FlushAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(new FlushRequested(completion)).ConfigureAwait(false);
        _flushNow.Release();
        await completion.Task.ConfigureAwait(false);
    }

    /// <summary>Stops accepting messages, then writes everything already queued.</summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            if (!_consumer.Wait(DrainTimeout))
            {
                Debug.WriteLine("Installed-application cache writer did not drain in time.");
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Installed-application cache writer failed while draining: {ex.Message}");
        }

        _shutdown.Dispose();
        _flushNow.Dispose();
    }

    private async Task ConsumeAsync()
    {
        var reader = _channel.Reader;
        var flushes = new List<TaskCompletionSource>();

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            try
            {
                // Waits for a batch to build up. A flush request or a shutdown ends the wait early.
                await _flushNow.WaitAsync(_batchWindow, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down: skip the wait, but still write what is queued.
            }

            while (reader.TryRead(out var message))
            {
                switch (message)
                {
                    case IconReady icon:
                        _pendingIcons[icon.AppUserModelId] = icon.PngBytes;
                        break;
                    case MetadataReady metadata:
                        _pendingEntries = metadata.Entries;
                        break;
                    case FlushRequested flush:
                        flushes.Add(flush.Completion);
                        break;
                }
            }

            Write();

            foreach (var flush in flushes)
            {
                flush.TrySetResult();
            }
            flushes.Clear();
        }
    }

    private void Write()
    {
        try
        {
            var entries = _pendingEntries ?? (_pendingIcons.Count > 0 ? _current.Values.ToArray() : null);
            if (entries is null)
            {
                return;
            }

            if (_pendingIcons.Count == 0 && HasSameMetadata(entries))
            {
                // Nothing changed since the last save. Skipping it saves rewriting the whole icon blob.
                return;
            }

            var icons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (_pendingIcons.TryGetValue(entry.AppUserModelId, out var fresh))
                {
                    icons[entry.AppUserModelId] = fresh;
                }
                else if (
                    _current.TryGetValue(entry.AppUserModelId, out var previous)
                    && previous.IconLength > 0
                    && previous.HasSameMetadataAs(entry)
                    && _store.LoadIconBytes(previous) is { } carried
                )
                {
                    // Save rebuilds the blob from this map, so an icon left out of it is lost.
                    icons[entry.AppUserModelId] = carried;
                }
            }

            _current = ToDictionary(_store.Save(entries, icons));
        }
        catch (Exception ex)
        {
            // One failed save must not end the consumer. The next batch tries again.
            Debug.WriteLine($"Failed to persist installed-application cache: {ex.Message}");
        }
        finally
        {
            _pendingEntries = null;
            _pendingIcons.Clear();
        }
    }

    private bool HasSameMetadata(IReadOnlyList<CachedApplicationEntry> entries)
    {
        if (entries.Count != _current.Count)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (!_current.TryGetValue(entry.AppUserModelId, out var existing) || !existing.HasSameMetadataAs(entry))
            {
                return false;
            }
        }

        return true;
    }

    // Last one wins, so a duplicate AUMID in a cache file cannot throw here.
    private static Dictionary<string, CachedApplicationEntry> ToDictionary(IReadOnlyList<CachedApplicationEntry> entries)
    {
        var result = new Dictionary<string, CachedApplicationEntry>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            result[entry.AppUserModelId] = entry;
        }

        return result;
    }
}
