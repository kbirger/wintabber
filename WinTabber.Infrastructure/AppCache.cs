using Microsoft.WindowsAPICodePack.Shell;
using ReactiveUI;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Reactive.Linq;
using System.Runtime.Caching;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTabber.Api.Media.ShellApplications.Models;

namespace WinTabberUI.Infrastructure;

public class AppCache
{

    private MemoryCache _aumidCache = new MemoryCache("AppCacheByAumid");
    private MemoryCache _processMapCache = new MemoryCache("AppCacheByProcessPath");


    public InstalledApplicationInfo GetByAumid(string key)
    {
        if (TryGetCachedApplication(key, out var app))
        {
            return app;
        }
        else
        {
            if (!TryGetShellObjectByParsingName(key, out var shellObject))
            {
                throw new KeyNotFoundException($"ShellObject with AUMID '{key}' not found.");
            }

            app = AddToCache(shellObject);

        }

        return app;
    }

    private InstalledApplicationInfo AddToCache(ShellObject shellObject)
    {
        InstalledApplicationInfo app;
        var name = shellObject.Name;
        var aumid = shellObject.ParsingName;

        // No real aumid; system gave file path
        if (Path.IsPathFullyQualified(aumid))
        {
            // use the filename
            aumid = Path.GetFileName(aumid);
        }

        var paths = GetShellObjectPaths(shellObject);

        var iconGetter = () => shellObject.Thumbnail.SmallBitmap;
        var iconObservable = Observable
            .Concat(LoadingImage, GetImageAsync(iconGetter))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Replay(1)
            .RefCount();

        app = new InstalledApplicationInfo
        {
            AppUserModelId = aumid,
            Name = name,
            TargetPath = paths.FirstOrDefault(),
            Icon = iconObservable
        };

        var exp = DateTimeOffset.UtcNow.AddDays(1);
        foreach (var path in paths)
        {
            _processMapCache.Set(path, aumid, exp);
        }
        _aumidCache.Add(aumid, app, exp);
        return app;
    }

    public InstalledApplicationInfo? GetByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        var canonicalPath = Path.GetFullPath(path);

        // Try to get the aumid from cache
        if (!TryGetCachedAumid(path, out var aumid))
        {
            // If not in cache, try to get the shell object by executable path path
            if (!TryGetShellObjectByPath(canonicalPath, out var shellObject))
            {
                return null;
            }


            aumid = shellObject.ParsingName;

            // The application, which contains an expensive to retrieve icon resource may already be cached
            if (!TryGetCachedApplication(aumid, out var app))
            {
                app = AddToCache(shellObject);

                return app;
            }
        }

        if (TryGetCachedApplication(aumid, out var app2))
        {
            return app2;
        }

        return null;
    }

    private IObservable<ImageSource> GetImageAsync(Func<System.Drawing.Bitmap> valueFactory)
    {
        return Observable.Defer(() => 
            Observable.Start(valueFactory, RxSchedulers.TaskpoolScheduler))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Select(Bitmap2BitmapImage)
            .Replay(1)
            .AutoConnect();
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private BitmapSource Bitmap2BitmapImage(Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
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
    private IObservable<ImageSource> LoadingImage { get; } = GetLoadingImage();

    private static IObservable<ImageSource> GetLoadingImage()
    {
        return Observable.Start(() =>
        {
            var uri = new Uri("pack://application:,,,/WinTabberUI;component/Images/loading.png");
            var src = new BitmapImage(uri);
            src.Freeze();

            return src;

        }, RxSchedulers.TaskpoolScheduler)
            .Replay(1)
            .AutoConnect();
    }

    private static IKnownFolder GetAppImageFolder()
    {
        var FOLDERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
        IKnownFolder appsFolder = KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);

        return appsFolder;
    }


    private bool TryGetCachedApplication(string aumid, [NotNullWhen(true)] out InstalledApplicationInfo? application)
    {
        var item = _aumidCache.Get(aumid);
        if (item is InstalledApplicationInfo app)
        {
            application = app;
            return true;
        }

        application = null;
        return false;
    }

    private bool TryGetCachedAumid(string path, [NotNullWhen(true)] out string? aumid)
    {
        var item = _processMapCache.Get(path);

        if (item is string value)
        {
            aumid = value;
            return true;
        }

        aumid = null;
        return false;
    }

    private bool TryGetShellObjectByPath(string path, [NotNullWhen(true)] out ShellObject? shellObject)
    {
        var canonicalPath = Path.GetFullPath(path);
        foreach (var item in GetAppImageFolder())
        {
            if (item is null)
            {
                continue;
            }
            var paths = GetShellObjectPaths(item).ToArray();

            var x = paths.Any(p => p.Contains("vmware"));
            var name = item.Name;
            var pn = item.ParsingName;
            var aumidx = item.Properties.System.AppUserModel.ID;
            if (paths.Any(path => string.Equals(path, canonicalPath, StringComparison.OrdinalIgnoreCase)))
            {
                shellObject = item;
                return true;

            }
        }

        shellObject = null;
        return false;
    }

    private IEnumerable<string> GetShellObjectPaths(ShellObject item)
    {
        // try to canonicalize paths by using Path.GetFullPath to compensate for paths being represented differently
        var targetParsingPath = item.Properties.System.Link.TargetParsingPath.Value;

        if (targetParsingPath is not null)
        {
            yield return Path.GetFullPath(targetParsingPath);
        }

        var packagePath = item.Properties.GetProperty<string>("System.AppUserModel.PackageInstallPath").Value;

        if (packagePath is not null)
        {
            yield return Path.GetFullPath(packagePath);
        }
    }

    private bool TryGetShellObjectByParsingName(string aumid, [NotNullWhen(true)] out ShellObject? shellObject)
    {
        foreach (var item in GetAppImageFolder())
        {
            var parsingPath = item.Properties.System.ParsingPath.Value;
            var parsingName = item.ParsingName;

            var x = item.Properties.DefaultPropertyCollection;
            var name = item.Name;
            if (string.Equals(parsingName, aumid, StringComparison.OrdinalIgnoreCase))
            {
                shellObject = item;
                return true;
            }
            if (parsingPath.Contains(@"\") && string.Equals(Path.GetFileName(aumid), Path.GetFileName(parsingName), StringComparison.OrdinalIgnoreCase))
            {
                shellObject = item;
                return true;
            }
        }

        shellObject = null;
        return false;
    }


    public void Load()
    {
        //_appFolder2 = _appFolder
        //    .ToDictionary(so => so.Properties.System.ParsingPath.Value);
    }
}
