using DynamicData;
using Microsoft.WindowsAPICodePack.Shell;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using WinTabber.Api.Media.ShellApplications.Caching;
using WinTabber.Api.Media.ShellApplications.Models;

namespace WinTabber.Api.Media.ShellApplications.Repositories;

public partial class InstalledApplicationRepository : IInstalledApplicationRepository
{

    private const string PackageInstallPath = "System.AppUserModel.PackageInstallPath";
    private static readonly HRESULT S_EXTRACTIONFAILED = (HRESULT)0x8004B200;

    private static readonly HRESULT S_PATHNOTFOUND = (HRESULT)0x8004B205;
    private readonly IShellApplicationSource _shellSource;
    private readonly IInstalledApplicationCacheStore _cacheStore;
    private readonly SourceCache<InstalledApplicationInfo, string> _apps = new(static app =>
        app.AppUserModelId
    );
    private IReadOnlyDictionary<string, CachedApplicationEntry> _previousEntriesByAumid =
        new Dictionary<string, CachedApplicationEntry>();
    private readonly IDisposable _liveAcquisitionSubscription;
    // ReplaySubject(1), not Subject: AsObservableCache() below subscribes eagerly in this
    // constructor, so background acquisition can fail and emit here before any consumer has had a
    // chance to subscribe — a plain Subject would drop that notification on the floor.
    private readonly ReplaySubject<Exception> _acquisitionErrors = new ReplaySubject<Exception>(1);

    // How long the cache writer holds a batch open. Icons that finish extracting inside this window
    // cost one save of the icon blob.
    private static readonly TimeSpan DefaultCacheBatchWindow = TimeSpan.FromSeconds(2);

    private readonly InstalledApplicationCacheWriter _cacheWriter;

    public InstalledApplicationRepository(IShellApplicationSource shellSource, IInstalledApplicationCacheStore cacheStore)
        : this(shellSource, cacheStore, DefaultCacheBatchWindow) { }

    // Internal, not an optional parameter on the public constructor: the DI container only sees the
    // public one, and a test passes TimeSpan.Zero so it never waits on a batch window.
    internal InstalledApplicationRepository(
        IShellApplicationSource shellSource,
        IInstalledApplicationCacheStore cacheStore,
        TimeSpan cacheBatchWindow
    )
    {
        _shellSource = shellSource;
        _cacheStore = cacheStore;

        // Seeded synchronously -- a small JSON read, fast enough not to delay construction -- so
        // ApplicationsByAumid/ApplicationsByPath have content immediately, before the live Shell
        // scan below (which runs on the task pool, in the background) produces anything.
        _apps.AddOrUpdate(LoadCachedApplications());

        // After the seed, so it starts from the entries actually on disk; before the scan below, so
        // MergeFreshApps can never run against a missing writer.
        _cacheWriter = new InstalledApplicationCacheWriter(
            cacheStore,
            _previousEntriesByAumid.Values.ToArray(),
            cacheBatchWindow
        );

        // DynamicData's Or() combinator (used below to merge this cache with its derived
        // partial/package/target caches) silently drops an OnError from its source instead of
        // propagating it to Connect() subscribers — confirmed with a reduced repro independent of
        // this class's own composition, not just an artifact of subscribing to the same cold
        // source multiple times. That risk no longer applies to _apps.Connect() itself: _apps is a
        // plain mutable cache that never errors, and every failure path below is fully absorbed by
        // Catch before this repository's own Subscribe -- Or() never sees it. AcquisitionErrors is
        // still how a consumer learns about it. Proven by
        // InstalledApplicationRepositoryTests.AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails.
        // Deliberately not .Publish().RefCount(): that combinator was carried over from the old code,
        // where it existed to share a single execution of an expensive, cold Observable.Start-based
        // Shell scan across multiple subscribers. _apps is now a hot, already-multicast SourceCache,
        // so Connect() is cheap and safe for every subscriber below to call independently -- and
        // multiple subscribers is exactly what happens here (ApplicationsByAumid, then
        // ApplicationsByPath's partial/package/target caches, all derived from primaryAumidCache).
        // With .Publish().RefCount(), only the first subscription would receive Connect()'s
        // synchronous cache-seed snapshot; every later subscription in this constructor would see
        // only future changes, leaving ApplicationsByPath empty until the next live update.
        var primaryAumidCache = _apps.Connect();

        // AutoRefreshOnObservable's reevaluator runs per item; a bare _apps.Connect() there would open one
        // connection (and replay the full snapshot) per item, which is O(N) connections each materializing
        // an N-item changeset. This trigger only needs to TICK when _apps changes -- the reevaluator
        // ignores the item passed to it (`_ =>`) -- so sharing one hot connection via Publish().RefCount()
        // is correct and cheap, unlike the sharing above (primaryAumidCache) that incorrectly drops the
        // seed snapshot for content-consuming subscribers.
        var refreshTrigger = _apps.Connect().Publish().RefCount();

        // TryPersistCache (not MergeFreshApps) is what's pushed off the inline scan-delivery path:
        // MergeFreshApps (fast, in-memory) runs directly here; TryPersistCache (which can block on a
        // full writer channel) is pushed to Task.Run inside MergeFreshApps so a slow persist never
        // delays the changeset this scan just produced from reaching ApplicationsByAumid's subscribers.
        //
        // A failed scan (the Catch below) reports on AcquisitionErrors and produces no further
        // action -- deliberately not a call to MergeFreshApps([]), which would otherwise remove
        // every cache-seeded entry (MergeFreshApps treats "not present in the fresh list" as
        // "uninstalled"). Proven by
        // InstalledApplicationRepositoryTests.ApplicationsByAumid_KeepsCachedEntries_WhenShellAcquisitionFails.
        _liveAcquisitionSubscription = GetInstalledApplicationsObservable()
            .Catch<IReadOnlyList<InstalledApplicationInfo>, Exception>(ex =>
            {
                _acquisitionErrors.OnNext(ex);
                return Observable.Empty<IReadOnlyList<InstalledApplicationInfo>>();
            })
            .Subscribe(MergeFreshApps);

        var partialAumidCache = primaryAumidCache
            .Filter(app => app.AppUserModelId.Contains(@"\"))
            .ChangeKey(app => Path.GetFileName(app.AppUserModelId));

        var packagePathCache = primaryAumidCache
            .Filter(app => app.PackageInstallPath is not null)
            .ChangeKey(app => app.PackageInstallPath!);

        var partialPackagePathCache = packagePathCache
            .Filter(app => app.PackageInstallPath!.Contains(@"\"))
            .ChangeKey(app => Path.GetFileName(app.PackageInstallPath!));

        var targetPathCache = primaryAumidCache
            .Filter(app => app.TargetPath is not null)
            .ChangeKey(app => app.TargetPath!);

        var partialTargetPathCache = targetPathCache
            .Filter(app => app.TargetPath!.Contains(@"\"))
            .ChangeKey(app => Path.GetFileName(app.TargetPath!));

        ApplicationsByAumid = primaryAumidCache.Or(partialAumidCache).AutoRefreshOnObservable(_ => refreshTrigger).AsObservableCache();

        ApplicationsByPath = partialAumidCache
            .AutoRefreshOnObservable(_ => refreshTrigger)
            .Or(partialPackagePathCache)
            .Or(targetPathCache)
            .Or(partialTargetPathCache)
            .AsObservableCache();
    }

    public void Dispose()
    {
        _liveAcquisitionSubscription.Dispose();
        // Drains the queue, so an icon extracted just before shutdown still reaches the disk.
        _cacheWriter.Dispose();
        ApplicationsByAumid.Dispose();
        ApplicationsByPath.Dispose();
        _apps.Dispose();
        _acquisitionErrors.Dispose();
    }

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

    private IReadOnlyList<InstalledApplicationInfo> LoadCachedApplications()
    {
        IReadOnlyList<CachedApplicationEntry> entries;
        try
        {
            entries = _cacheStore.Load();

            // Tolerate duplicate AUMIDs in the cache file (last one wins) instead of throwing --
            // GetAumid collapses any backslash-containing ParsingName to just its filename, so two
            // distinct Shell items (e.g. a per-user and a per-machine shortcut with the same
            // filename) can legitimately produce the same AUMID during a scan. A corrupt/duplicate
            // cache file must degrade to empty, not crash every future launch of the app.
            _previousEntriesByAumid = entries
                .GroupBy(entry => entry.AppUserModelId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load installed-application cache: {ex.Message}");
            entries = [];
            _previousEntriesByAumid = new Dictionary<string, CachedApplicationEntry>();
        }

        return entries
            .Select(entry => new InstalledApplicationInfo
            {
                AppUserModelId = entry.AppUserModelId,
                Name = entry.Name,
                TargetPath = entry.TargetPath,
                PackageInstallPath = entry.PackageInstallPath,
                Icon = GetCachedIcon(entry),
            })
            .ToArray();
    }

    private IObservable<Bitmap?> GetCachedIcon(CachedApplicationEntry entry)
    {
        return Observable
            .Defer(() => Observable.Start(() => _cacheStore.LoadIcon(entry), TaskPoolScheduler.Default))
            .Replay(1)
            .AutoConnect();
    }

    /// <summary>
    /// Replaces <see cref="_apps"/>'s contents with <paramref name="freshApps"/>: an app not in
    /// <paramref name="freshApps"/> is removed (it is no longer installed), an app already present
    /// is overwritten with the fresh value regardless of <see cref="InstalledApplicationInfo.Equals"/>
    /// (which compares only the AUMID, so it cannot be relied on to detect a changed Icon/TargetPath/Name),
    /// and a new app is added. Deliberately not built on <c>ToObservableChangeSet</c> over a
    /// concatenated cached-then-fresh sequence -- that operator was verified (see the plan's "Design
    /// decisions verified empirically" section) to be additive only, never removing a key absent from
    /// a later emission, which would leave an uninstalled app in the cache until its 1-day expiry.
    /// Internal so a test can drive it directly without needing a real successful Shell scan (not
    /// producible from a test -- <c>ShellObject</c> has no accessible constructor; see this project's
    /// README.md).
    /// </summary>
    internal void MergeFreshApps(IReadOnlyList<InstalledApplicationInfo> freshApps)
    {
        // An empty fresh list is untrustworthy, not a legitimate "everything got uninstalled"
        // result: the live Shell scan runs exactly once per process, so there is no later scan
        // that could ever correct a mistakenly-emptied cache. Discarding a good seeded cache in
        // favor of an empty result is worse than doing nothing, so treat it as a no-op.
        if (freshApps.Count == 0)
        {
            return;
        }

        var freshKeys = new HashSet<string>(freshApps.Select(app => app.AppUserModelId), StringComparer.Ordinal);
        _apps.Edit(updater =>
        {
            var staleKeys = updater.Keys.Where(key => !freshKeys.Contains(key)).ToArray();
            updater.Remove(staleKeys);
            updater.AddOrUpdate(freshApps);
        });

        _ = Task.Run(() => TryPersistCache(freshApps));
    }

    /// <summary>
    /// Queues the app list for the cache writer. Internal so a test can call it directly, instead of
    /// through the fire-and-forget <see cref="Task.Run(Action)"/> <see cref="MergeFreshApps"/> wraps it in.
    /// Saves metadata only. It never extracts or decodes an icon: most installed apps never have a
    /// media session, so an icon is extracted when something first asks for it (see
    /// <see cref="CreateIcon"/>) and reaches the cache from there. The writer keeps the icons already
    /// on disk for every app whose metadata did not change.
    /// </summary>
    internal void TryPersistCache(IReadOnlyList<InstalledApplicationInfo> freshApps)
    {
        try
        {
            // Deduped by AppUserModelId (last one wins) so a Shell scan that produces two entries
            // with the same AUMID (GetAumid collapses any backslash-containing ParsingName to just
            // its filename, so a per-user and a per-machine shortcut with the same filename collide)
            // never gets written to the cache file -- LoadCachedApplications tolerates a duplicate
            // that's already on disk, but this process must never produce one in the first place.
            var entriesByAumid = new Dictionary<string, CachedApplicationEntry>(freshApps.Count, StringComparer.Ordinal);
            foreach (var app in freshApps)
            {
                entriesByAumid[app.AppUserModelId] = new CachedApplicationEntry
                {
                    AppUserModelId = app.AppUserModelId,
                    Name = app.Name,
                    TargetPath = app.TargetPath,
                    PackageInstallPath = app.PackageInstallPath,
                };
            }

            _cacheWriter.QueueMetadata(entriesByAumid.Values.ToArray());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to persist installed-application cache: {ex.Message}");
        }
    }

    /// <summary>Completes once everything queued so far is on disk. For tests.</summary>
    internal Task FlushCacheAsync() => _cacheWriter.FlushAsync();

    /// <summary>
    /// The icon for one installed app: the cache first, then Shell. Nothing runs until the first
    /// subscription, and <c>Replay(1)</c>/<c>AutoConnect()</c> makes that extraction happen once per
    /// process.
    /// The cache is trusted only if the app is unchanged since it was saved. A moved or renamed app
    /// gets a new icon from Shell.
    /// </summary>
    /// <param name="shellExtraction">
    /// The cold Shell extraction. It runs only when the cache has no usable icon, and it queues its
    /// result for the cache writer.
    /// </param>
    internal IObservable<Bitmap?> CreateIcon(
        string appUserModelId,
        string name,
        string? targetPath,
        string? packageInstallPath,
        IObservable<Bitmap?> shellExtraction
    )
    {
        if (
            !_previousEntriesByAumid.TryGetValue(appUserModelId, out var previous)
            || previous.IconLength <= 0
            || !previous.HasSameMetadataAs(name, targetPath, packageInstallPath)
        )
        {
            return shellExtraction.Replay(1).AutoConnect();
        }

        return Observable
            .Start(() => _cacheStore.LoadIcon(previous), TaskPoolScheduler.Default)
            // A null here is a corrupt or truncated blob. Treat it as a cache miss.
            .SelectMany(cached => cached is not null ? Observable.Return<Bitmap?>(cached) : shellExtraction)
            .Replay(1)
            .AutoConnect();
    }

    /// <summary>
    /// False when no icon can exist for <paramref name="path"/>, so Shell is never asked. Shell
    /// answers a missing file with an exception, and an exception per dead entry is expensive noise.
    /// </summary>
    internal static bool CanHaveIcon([NotNullWhen(true)] string? path) =>
        !string.IsNullOrWhiteSpace(path)
        // A shell namespace item (Explorer, the Run dialog). It is not a file path.
        && !path.StartsWith("::", StringComparison.Ordinal)
        && (File.Exists(path) || Directory.Exists(path));

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

    private static bool IsValid([NotNullWhen(true)] ShellObject? shellObject)
    {
        return shellObject is not null
            && !string.IsNullOrWhiteSpace(shellObject.Name)
            && !string.IsNullOrWhiteSpace(shellObject.ParsingName);
    }

    private InstalledApplicationInfo CreateItem(ShellObject shellObject)
    {
        string? targetParsingPath = shellObject.Properties.System.Link.TargetParsingPath.Value;
        string? packageInstallPath = shellObject
            .Properties.GetProperty<string>(PackageInstallPath)
            .Value;
        string? path = packageInstallPath ?? targetParsingPath;
        var aumid = GetAumid(shellObject);
        return new InstalledApplicationInfo
        {
            AppUserModelId = aumid,
            Icon = CreateIcon(
                aumid,
                shellObject.Name,
                targetParsingPath,
                packageInstallPath,
                GetIcon(shellObject, path, aumid)
            ),
            Name = shellObject.Name,
            TargetPath = targetParsingPath,
            PackageInstallPath = packageInstallPath,
        };
    }

    private IObservable<Bitmap?> GetIcon(ShellObject shellObject, string? path, string appUserModelId)
    {
        int width = (int)shellObject.Thumbnail.CurrentSize.Width;
        int height = (int)shellObject.Thumbnail.CurrentSize.Height;
        ThumbnailOptions options = ThumbnailOptions.None;
        return Observable
            .Defer(() =>
                Observable.Start<Bitmap?>(
                    () =>
                    {
                        // No file, no icon. Skipping Shell here also skips the exception it throws.
                        if (!CanHaveIcon(path))
                        {
                            return null;
                        }

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

                            // Not Image.FromHbitmap: it always produces Format32bppRgb, discarding
                            // whatever alpha channel the source HBITMAP has -- a well-documented
                            // GDI+ limitation, confirmed live as a solid black box behind every icon
                            // that should have had a transparent background. IShellItemImageFactory
                            // returns a 32bpp ARGB DIB section for icon/thumbnail requests, so the
                            // alpha byte is there; it just needs to be read directly instead.
                            try
                            {
                                var bitmap = CreateBitmapPreservingAlpha(hBitmap);
                                QueueIconForCache(appUserModelId, bitmap);
                                return bitmap;
                            }
                            finally
                            {
                                Windows.Win32.ShellPInvoke.DeleteObject(new HGDIOBJ((nint)hBitmap));
                            }
                        }
                    },
                    // Not Scheduler.CurrentThread: that runs the extraction on whichever thread
                    // subscribes, and a view can subscribe from the UI thread. The extraction is COM
                    // and file I/O (CanHaveIcon can stall on a network path), so it belongs on the pool.
                    TaskPoolScheduler.Default
                )
            );
    }

    /// <summary>
    /// Encodes on the calling thread, before <paramref name="bitmap"/> is shared. After that a UI
    /// converter also saves it, and a GDI+ bitmap must not be used from two threads at once. The
    /// writer gets bytes only. An encode failure costs the cache entry, never the icon.
    /// </summary>
    private void QueueIconForCache(string appUserModelId, Bitmap bitmap)
    {
        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            _cacheWriter.TryQueueIcon(appUserModelId, stream.ToArray());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to queue icon for {appUserModelId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads an HBITMAP's pixel data directly into a Format32bppArgb <see cref="Bitmap"/>, preserving
    /// alpha -- unlike <see cref="Image.FromHbitmap(nint)"/>, which always produces Format32bppRgb.
    /// The constructed Bitmap wraps <c>bmp.bmBits</c> directly (not a copy), so it is cloned before
    /// returning: <c>bmp.bmBits</c> points into the HBITMAP's own DIB section, which the caller frees
    /// immediately after this returns.
    /// </summary>
    private static unsafe Bitmap CreateBitmapPreservingAlpha(HBITMAP hBitmap)
    {
        Windows.Win32.Graphics.Gdi.BITMAP bmp;
        Windows.Win32.ShellPInvoke.GetObject(
            new HGDIOBJ((nint)hBitmap),
            sizeof(Windows.Win32.Graphics.Gdi.BITMAP),
            &bmp
        );
        using var view = new Bitmap(bmp.bmWidth, bmp.bmHeight, bmp.bmWidthBytes, PixelFormat.Format32bppArgb, (nint)bmp.bmBits);
        return new Bitmap(view);
    }

    private static string GetAumid(ShellObject shellObject)
    {
        return shellObject.ParsingName switch
        {
            string name when name.Contains(@"\") => Path.GetFileName(name),
            string name => name,
        };
    }

    public static IObservable<Bitmap?> LoadingImage { get; } = GetLoadingImage();
    public IObservableCache<InstalledApplicationInfo, string> ApplicationsByAumid { get; }
    public IObservableCache<InstalledApplicationInfo, string> ApplicationsByPath { get; }
    public IObservable<Exception> AcquisitionErrors => _acquisitionErrors;

    private static IObservable<Bitmap?> GetLoadingImage()
    {
        return Observable.Start(
            () => (Bitmap?)null,
            TaskPoolScheduler.Default
        );
    }
}
