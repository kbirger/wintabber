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
    private readonly Subject<Unit> _refreshSubject = new Subject<Unit>();
    // ReplaySubject(1), not Subject: AsObservableCache() below subscribes eagerly in this
    // constructor, so background acquisition can fail and emit here before any consumer has had a
    // chance to subscribe — a plain Subject would drop that notification on the floor.
    private readonly ReplaySubject<Exception> _acquisitionErrors = new ReplaySubject<Exception>(1);

    public void Refresh()
    {
        _refreshSubject.OnNext(Unit.Default);
    }

    public InstalledApplicationRepository(IShellApplicationSource shellSource, IInstalledApplicationCacheStore cacheStore)
    {
        _shellSource = shellSource;
        _cacheStore = cacheStore;

        // Seeded synchronously -- a small JSON read, fast enough not to delay construction -- so
        // ApplicationsByAumid/ApplicationsByPath have content immediately, before the live Shell
        // scan below (which runs on the task pool, in the background) produces anything.
        _apps.AddOrUpdate(LoadCachedApplications());

        // DynamicData's Or() combinator (used below to merge this cache with its derived
        // partial/package/target caches) silently drops an OnError from its source instead of
        // propagating it to Connect() subscribers — confirmed with a reduced repro independent of
        // this class's own composition, not just an artifact of subscribing to the same cold
        // source multiple times. That risk no longer applies to _apps.Connect() itself: _apps is a
        // plain mutable cache that never errors, and every failure path below is fully absorbed by
        // Catch before this repository's own Subscribe -- Or() never sees it. AcquisitionErrors is
        // still how a consumer learns about it. Proven by
        // InstalledApplicationRepositoryTests.AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails.
        var primaryAumidCache = _apps.Connect().Publish().RefCount();

        // MergeFreshApps and TryPersistCache are NOT run inline with this scan -- MergeFreshApps
        // (fast, in-memory) runs directly here; TryPersistCache (icon PNG-encoding, disk I/O) is
        // pushed to Task.Run inside MergeFreshApps so a slow persist never delays the changeset this
        // scan just produced from reaching ApplicationsByAumid's subscribers.
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

        ApplicationsByAumid = primaryAumidCache.Or(partialAumidCache).AutoRefreshOnObservable(_ => primaryAumidCache).AsObservableCache();

        ApplicationsByPath = partialAumidCache
            .AutoRefreshOnObservable(_ => primaryAumidCache)
            .Or(partialPackagePathCache)
            .Or(targetPathCache)
            .Or(partialTargetPathCache)
            .AsObservableCache();
    }

    private IObservable<Unit> GetRefreshEvents()
    {
        return Observable.Merge(
            Observable.Interval(TimeSpan.FromMinutes(30)).Select(_ => Unit.Default),
            _refreshSubject
        );
    }

    public void Dispose()
    {
        _liveAcquisitionSubscription.Dispose();
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load installed-application cache: {ex.Message}");
            entries = [];
        }

        _previousEntriesByAumid = entries.ToDictionary(entry => entry.AppUserModelId);

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
        var freshKeys = new HashSet<string>(freshApps.Select(app => app.AppUserModelId), StringComparer.Ordinal);
        _apps.Edit(updater =>
        {
            var staleKeys = updater.Keys.Where(key => !freshKeys.Contains(key)).ToArray();
            updater.Remove(staleKeys);
            updater.AddOrUpdate(freshApps);
        });

        if (freshApps.Count > 0)
        {
            _ = Task.Run(() => TryPersistCache(freshApps));
        }
    }

    /// <summary>
    /// Internal so a test can call it directly and synchronously, instead of through the
    /// fire-and-forget <see cref="Task.Run(Action)"/> <see cref="MergeFreshApps"/> wraps it in.
    /// </summary>
    internal void TryPersistCache(IReadOnlyList<InstalledApplicationInfo> freshApps)
    {
        try
        {
            var entries = new List<CachedApplicationEntry>(freshApps.Count);
            var iconBytesByAumid = new Dictionary<string, byte[]?>(freshApps.Count);

            foreach (var app in freshApps)
            {
                entries.Add(new CachedApplicationEntry
                {
                    AppUserModelId = app.AppUserModelId,
                    Name = app.Name,
                    TargetPath = app.TargetPath,
                    PackageInstallPath = app.PackageInstallPath,
                });

                if (
                    _previousEntriesByAumid.TryGetValue(app.AppUserModelId, out var previous)
                    && previous.Name == app.Name
                    && previous.TargetPath == app.TargetPath
                    && previous.PackageInstallPath == app.PackageInstallPath
                )
                {
                    // Unchanged since the last successful write -- reuse the icon bytes already on
                    // disk instead of forcing GetIcon's COM extraction to run again. Without this,
                    // every launch would eagerly decode every installed app's icon up front, turning
                    // today's lazy, on-demand load into a full-catalog decode -- the opposite of what
                    // this cache exists to avoid.
                    iconBytesByAumid[app.AppUserModelId] = _cacheStore.LoadIconBytes(previous);
                }
                else
                {
                    iconBytesByAumid[app.AppUserModelId] = TryEncodeIcon(app.Icon);
                }
            }

            _cacheStore.Save(entries, iconBytesByAumid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to persist installed-application cache: {ex.Message}");
        }
    }

    private static byte[]? TryEncodeIcon(IObservable<Bitmap?> icon)
    {
        try
        {
            var bitmap = icon.Timeout(TimeSpan.FromSeconds(5)).FirstOrDefaultAsync().Wait();
            if (bitmap is null)
            {
                return null;
            }

            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            return memoryStream.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
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
        return new InstalledApplicationInfo
        {
            AppUserModelId = GetAumid(shellObject),
            //Icon = Observable.Defer(() => Observable.Concat(LoadingImage, GetIcon(shellObject))),
            Icon = GetIcon(shellObject, path),
            Name = shellObject.Name,
            TargetPath = targetParsingPath,
            PackageInstallPath = packageInstallPath,
        };
    }

    private IObservable<Bitmap?> GetIcon(ShellObject shellObject, string path)
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

                            // Not Image.FromHbitmap: it always produces Format32bppRgb, discarding
                            // whatever alpha channel the source HBITMAP has -- a well-documented
                            // GDI+ limitation, confirmed live as a solid black box behind every icon
                            // that should have had a transparent background. IShellItemImageFactory
                            // returns a 32bpp ARGB DIB section for icon/thumbnail requests, so the
                            // alpha byte is there; it just needs to be read directly instead.
                            try
                            {
                                return CreateBitmapPreservingAlpha(hBitmap);
                            }
                            finally
                            {
                                Windows.Win32.ShellPInvoke.DeleteObject(new HGDIOBJ((nint)hBitmap));
                            }
                        }
                    },
                    Scheduler.CurrentThread
                )
            )
            .Replay(1)
            .AutoConnect();
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
