using Microsoft.WindowsAPICodePack.Shell;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinTabberUI.Infrastructure;

public class ImageCache
{
    
    private MemoryCache _cache = new MemoryCache("ImageCache");
    private IKnownFolder _appFolder = GetAppImageFolder();
    private IDictionary<string, ShellObject> _appFolder2;

    public IObservable<ImageSource> GetOrAddAsync(string key, Func<System.Drawing.Bitmap> valueFactory)
    {
        return GetOrAddAsync(key, async () => valueFactory());

    }
    public IObservable<ImageSource> GetOrAddAsync(string key, Func<Task<System.Drawing.Bitmap>> valueFactory)
    {
        if (_cache.Get(key) is IObservable<ImageSource> cachedObservable)
        {
            return cachedObservable;
        }

        var newValue = GetImageAsync(valueFactory);
        _cache.Add(key, newValue, DateTimeOffset.UtcNow.AddHours(1));
        return newValue;
    }

    private IObservable<ImageSource> GetImageAsync(Func<Task<System.Drawing.Bitmap>> valueFactory)
    {
        return Observable.Merge(
            LoadingImage,
            Observable.FromAsync(valueFactory, RxApp.TaskpoolScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(Bitmap2BitmapImage)
        )
        .ObserveOn(RxApp.MainThreadScheduler)
        
        .Replay(1)
        .RefCount();
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

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
        }
        finally
        {
            DeleteObject(hBitmap);
        }

        return retval;
    }
    public IObservable<ImageSource> LoadingImage { get; } = GetLoadingImage();

    private static IObservable<ImageSource> GetLoadingImage()
    {
        var uri = new Uri("pack://application:,,,/WinTabberUI;component/Images/loading.png");
        var src = new BitmapImage(uri);
        src.Freeze();

        return Observable.Return(src);
    }

    public IKnownFolder AppFolder => _appFolder;
    public IDictionary<string, ShellObject> AppFolder2 => _appFolder2;

    private static IKnownFolder GetAppImageFolder()
    {
        var FOLDERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
        IKnownFolder appsFolder = KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);

        return appsFolder;
    }

    internal void Load()
    {
        _appFolder2 = AppFolder
            .ToDictionary(so => so.Properties.System.ParsingPath.Value);
    }
}
