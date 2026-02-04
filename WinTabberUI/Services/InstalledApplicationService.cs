using DynamicData;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.Xaml.Behaviors.Media;
using MS.WindowsAPICodePack.Internal;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using WinTabberUI.Infrastructure;
using WinTabberUI.ViewModels;
using static System.Windows.Forms.Design.AxImporter;

namespace WinTabberUI.Services;

public class InstalledApplicationService : IDisposable
{
    [Flags]
    public enum ThumbnailOptions
    {
        None = 0x00,
        BiggerSizeOk = 0x01,
        InMemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }
    private const string PackageInstallPath = "System.AppUserModel.PackageInstallPath";
    private static readonly Guid FOLDERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
    private static readonly Guid GUID_IShellItem = typeof(IShellItem).GUID;
    private static readonly HRESULT S_EXTRACTIONFAILED = (HRESULT)0x8004B200;

    private static readonly HRESULT S_PATHNOTFOUND = (HRESULT)0x8004B205;
    private readonly SourceCache<InstalledApplicationInfo, string> _apps = new(app => app.AppUserModelId);
    private CompositeDisposable? _cleanup;

    public InstalledApplicationService()
    {
        var primaryAumidCache = GetInstalledApplicationsObservable()
            .ToObservableChangeSet(
                keySelector: app => app.AppUserModelId,
                expireAfter: item => TimeSpan.FromDays(1))
            .AsObservableCache();

        var partialAumidCache = primaryAumidCache
            .Connect()
            .Filter(app => app.AppUserModelId.Contains(@"\"))
            .ChangeKey(app => Path.GetFileName(app.AppUserModelId));

        var packagePathCache = primaryAumidCache
            .Connect()
            .Filter(app => app.PackageInstallPath is not null)
            .ChangeKey(app => app.PackageInstallPath!);

        var partialPackagePathCache = packagePathCache
            .Filter(app => app.PackageInstallPath!.Contains(@"\"))
            .ChangeKey(app => Path.GetFileName(app.PackageInstallPath!));

        var targetPathCache = primaryAumidCache
            .Connect()
            .Filter(app => app.TargetPath is not null)
            .ChangeKey(app => app.TargetPath!);

        var partialTargetPathCache = targetPathCache
            .Filter(app => app.TargetPath!.Contains(@"\"))
            .ChangeKey(app => Path.GetFileName(app.TargetPath!));


        ApplicationsByAumid = primaryAumidCache.Connect()
            .Or(partialAumidCache)
            .AsObservableCache();

        ApplicationsByPath = partialAumidCache
            .Or(partialPackagePathCache)
            .Or(targetPathCache)
            .Or(partialTargetPathCache)
            .AsObservableCache();

        _cleanup = new CompositeDisposable(primaryAumidCache, ApplicationsByAumid, ApplicationsByPath);
    }


    public void Dispose()
    {
        _cleanup?.Dispose();
    }

    private static IObservable<IReadOnlyList<InstalledApplicationInfo>> GetInstalledApplicationsObservable()
    {
        return Observable.Start<IReadOnlyList<InstalledApplicationInfo>>(() =>
        {
            return GetInstalledApplicationsBlocking().ToArray();
        }, RxApp.TaskpoolScheduler);
    }

    private static IKnownFolder GetAppImageFolder()
    {
        IKnownFolder appsFolder = KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);

        return appsFolder;
    }

    private static IEnumerable<InstalledApplicationInfo> GetInstalledApplicationsBlocking()
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
        Debug.WriteLine($"Loaded installed applications in {sw.ElapsedMilliseconds} ms on thread {Thread.CurrentThread.ManagedThreadId}");
    }

    private static bool IsValid([NotNullWhen(true)] ShellObject? shellObject)
    {
        return shellObject is not null
            && !string.IsNullOrWhiteSpace(shellObject.Name)
            && !string.IsNullOrWhiteSpace(shellObject.ParsingName);
    }

    private static InstalledApplicationInfo CreateItem(ShellObject shellObject)
    {
        string? targetParsingPath = shellObject.Properties.System.Link.TargetParsingPath.Value;
        string? packageInstallPath = shellObject.Properties.GetProperty<string>(PackageInstallPath).Value;
        string? path = packageInstallPath ?? targetParsingPath;
        return new InstalledApplicationInfo
        {
            AppUserModelId = GetAumid(shellObject),
            //Icon = Observable.Defer(() => Observable.Concat(LoadingImage, GetIcon(shellObject))),
            Icon = GetIcon(shellObject, path),
            Name = shellObject.Name,
            TargetPath = targetParsingPath,
            PackageInstallPath = packageInstallPath
        };
    }


    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static BitmapSource Bitmap2BitmapImage(Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap(System.Drawing.Color.Red);
        BitmapSource retval;

        try
        {
            retval = Imaging.CreateBitmapSourceFromHBitmap(
                         hBitmap,
                         IntPtr.Zero,
                         Int32Rect.Empty,
                         BitmapSizeOptions.FromEmptyOptions());
            retval.Freeze();
        }
        finally
        {
            DeleteObject(hBitmap);
        }

        return retval;
    }

    private static IObservable<ImageSource> GetIcon(ShellObject shellObject, string path)
    {
        //var bitmap = shellObject.Thumbnail.LargeBitmap;
        //var z = () => shellObject.Thumbnail.LargeBitmapSource;
        //var zz =  Bitmap2BitmapImage(bitmap);
        int width = (int)shellObject.Thumbnail.CurrentSize.Width;
        int height = (int)shellObject.Thumbnail.CurrentSize.Height;
        ThumbnailOptions options = ThumbnailOptions.None;
        return Observable.Defer(() =>
            Observable.Start(() =>
            {
                unsafe
                {

                    PInvoke.SHCreateItemFromParsingName(path, null, GUID_IShellItem, out var nativeShellItem).ThrowOnFailure();

                    if (nativeShellItem is not IShellItemImageFactory imageFactory)
                    {
                        Marshal.ReleaseComObject(nativeShellItem);
                        nativeShellItem = null;
                        throw new InvalidOperationException("Failed to get IShellItemImageFactory");
                    }

                    SIZE size = new SIZE
                    {
                        cx = width,
                        cy = height
                    };

                    HBITMAP hBitmap = default;
                    try
                    {
                        try
                        {
                            imageFactory.GetImage(size, (SIIGBF)options, &hBitmap);
                        }
                        catch (COMException ex) when (options == ThumbnailOptions.ThumbnailOnly &&
                            (ex.HResult == S_PATHNOTFOUND || ex.HResult == S_EXTRACTIONFAILED))
                        {
                            // Fallback to IconOnly if extraction fails or files cannot be found
                            imageFactory.GetImage(size, (SIIGBF)ThumbnailOptions.IconOnly, &hBitmap);
                        }
                        catch (FileNotFoundException) when (options == ThumbnailOptions.ThumbnailOnly)
                        {
                            // Fallback to IconOnly if files cannot be found
                            imageFactory.GetImage(size, (SIIGBF)ThumbnailOptions.IconOnly, &hBitmap);
                        }
                        catch (System.Exception ex)
                        {
                            // Handle other exceptions
                            throw new InvalidOperationException("Failed to get thumbnail", ex);
                        }
                    }
                    finally
                    {
                        if (nativeShellItem != null)
                        {
                            Marshal.ReleaseComObject(nativeShellItem);
                        }
                    }

                    var image = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    if (!image.IsFrozen && image.CanFreeze)
                    {
                        image.Freeze();
                    }

                    return image;
                }
                //PInvoke.sh
                //var image = z;
                //var image = Bitmap2BitmapImage(bitmap);

                //if (!image.IsFrozen && image.CanFreeze)
                //{
                    //image.Freeze();
                //}

                //return image;
            }, RxApp.MainThreadScheduler))
            .Replay(1)
            .AutoConnect();
    }

    private static string GetAumid(ShellObject shellObject)
    {
        return shellObject.ParsingName switch
        {
            string name when name.Contains(@"\") => Path.GetFileName(name),
            string name => name
        };
    }

    public static IObservable<ImageSource> LoadingImage { get; } = GetLoadingImage();
    public IObservableCache<InstalledApplicationInfo, string> ApplicationsByAumid { get; }
    public IObservableCache<InstalledApplicationInfo, string> ApplicationsByPath { get; }

    private static IObservable<ImageSource> GetLoadingImage()
    {
        return Observable.Start(() =>
        {

            //var uri = new Uri("pack://application:,,,/WinTabberUI;component/Images/loading.png");
            var src = new BitmapImage();
            src.Freeze();

            return src;

        }, RxApp.TaskpoolScheduler);
    }
}
