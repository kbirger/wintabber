using DynamicData;
using Microsoft.WindowsAPICodePack.Shell;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using WinTabber.Api.Media.ShellApplications.Models;

namespace WinTabber.Api.Media.ShellApplications.Repositories;

public partial class InstalledApplicationRepository : IInstalledApplicationRepository
{

    private const string PackageInstallPath = "System.AppUserModel.PackageInstallPath";
    private static readonly HRESULT S_EXTRACTIONFAILED = (HRESULT)0x8004B200;

    private static readonly HRESULT S_PATHNOTFOUND = (HRESULT)0x8004B205;
    private readonly IShellApplicationSource _shellSource;
    private readonly SourceCache<InstalledApplicationInfo, string> _apps = new(static app =>
        app.AppUserModelId
    );
    private readonly Subject<Unit> _refreshSubject = new Subject<Unit>();
    // ReplaySubject(1), not Subject: AsObservableCache() below subscribes eagerly in this
    // constructor, so background acquisition can fail and emit here before any consumer has had a
    // chance to subscribe — a plain Subject would drop that notification on the floor.
    private readonly ReplaySubject<Exception> _acquisitionErrors = new ReplaySubject<Exception>(1);

    public void Refresh()
    {
        _refreshSubject.OnNext(Unit.Default);
    }

    public InstalledApplicationRepository(IShellApplicationSource shellSource)
    {
        _shellSource = shellSource;
        //var primaryAumidCache = GetRefreshEvents()
        //    .StartWith(Unit.Default)
        //    .ExhaustMap(_ => GetInstalledApplicationsObservable())
        // DynamicData's Or() combinator (used below to merge this cache with its derived
        // partial/package/target caches) silently drops an OnError from its source instead of
        // propagating it to Connect() subscribers — confirmed with a reduced repro independent of
        // this class's own composition, not just an artifact of subscribing to the same cold
        // source multiple times. So a failure is caught here, before it ever reaches Or(), and
        // reported on AcquisitionErrors instead; the changeset itself completes as if acquisition
        // returned an empty list, keeping Or()/AutoRefreshOnObservable on their normal path.
        // Publish().RefCount() then shares that one execution across every downstream subscriber
        // (Or() directly, AutoRefreshOnObservable, and the partial/package/target caches derived
        // from it) so a failure is reported once, not once per subscriber. Proven by
        // InstalledApplicationRepositoryTests.AcquisitionErrors_Emits_WhenAppsFolderAcquisitionFails.
        var primaryAumidCache = GetInstalledApplicationsObservable()
            .Catch<IReadOnlyList<InstalledApplicationInfo>, Exception>(ex =>
            {
                _acquisitionErrors.OnNext(ex);
                return Observable.Return<IReadOnlyList<InstalledApplicationInfo>>([]);
            })
            .ToObservableChangeSet(
                keySelector: app => app.AppUserModelId,
                expireAfter: item => TimeSpan.FromDays(1)
            )
            .Publish()
            .RefCount();

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
        ApplicationsByAumid.Dispose();
        ApplicationsByPath.Dispose();
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

    private static string GetAumid(ShellObject shellObject)
    {
        return shellObject.ParsingName switch
        {
            string name when name.Contains(@"\") => Path.GetFileName(name),
            string name => name,
        };
    }

    public static IObservable<ImageSource> LoadingImage { get; } = GetLoadingImage();
    public IObservableCache<InstalledApplicationInfo, string> ApplicationsByAumid { get; }
    public IObservableCache<InstalledApplicationInfo, string> ApplicationsByPath { get; }
    public IObservable<Exception> AcquisitionErrors => _acquisitionErrors;

    private static IObservable<ImageSource> GetLoadingImage()
    {
        return Observable.Start(
            () =>
            {
                //var uri = new Uri("pack://application:,,,/WinTabberUI;component/Images/loading.png");
                var src = new BitmapImage();
                src.Freeze();

                return src;
            },
            TaskPoolScheduler.Default
        );
    }
}
