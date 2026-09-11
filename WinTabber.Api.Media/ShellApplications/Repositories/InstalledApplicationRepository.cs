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
using WinTabber.Api.Media.ShellApplications;
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
        var primaryAumidCache = GetInstalledApplicationsObservable()
            .ToObservableChangeSet(
                keySelector: app => app.AppUserModelId,
                expireAfter: item => TimeSpan.FromDays(1)
            );

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
