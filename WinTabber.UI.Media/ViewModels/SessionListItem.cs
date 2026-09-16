using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.ViewModels;

public class SessionListItem : ReactiveObject, IEquatable<SessionListItem>
{
    public SessionListItem(AggregateSession session)
    {
        Name = session.App.Name;
        // ObserveOn before Select, not after: ToImageSource calls Bitmap.GetHbitmap(), and
        // System.Drawing.Bitmap (unlike a frozen WPF ImageSource) is not safe to touch off the
        // thread that owns it. The cached Icon observable can be shared by more than one
        // subscriber, so the emitting thread must not be assumed to be the UI thread.
        _icon = session.App.Icon
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(ToImageSource)
            .ToProperty(this, vm  => vm.Icon, scheduler: RxApp.MainThreadScheduler);
        Aumid = session.MediaSession.SourceAppUserModelId;
        Session = session;

        _icon.ThrownExceptions.Subscribe(ex =>
        {
            Debug.WriteLine("Error getting session app icon {0}", ex);
        });

    }

    /// <summary>
    /// Converts the framework-neutral <see cref="System.Drawing.Bitmap"/> the shell app
    /// repository reports into a WPF-bindable <see cref="ImageSource"/>. A WinUI 3 consumer of
    /// the same <c>Icon</c> observable decodes into its own <c>BitmapImage</c> instead.
    /// </summary>
    private static ImageSource? ToImageSource(System.Drawing.Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        nint hBitmap = bitmap.GetHbitmap();
        try
        {
            var image = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );
            if (!image.IsFrozen && image.CanFreeze)
            {
                image.Freeze();
            }
            return image;
        }
        finally
        {
            Windows.Win32.PInvoke.DeleteObject(new Windows.Win32.Graphics.Gdi.HGDIOBJ(hBitmap));
        }
    }

    public AggregateSession Session { get; init; }
    private readonly ObservableAsPropertyHelper<ImageSource?> _icon;
    public string Name { get; init; }
    public ImageSource? Icon => _icon.Value;
    public string Aumid { get; init; }

    public bool Equals(SessionListItem? other)
    {
        return string.Equals(other?.Aumid, Aumid, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as SessionListItem);
    }

    public override int GetHashCode()
    {
        return Aumid.GetHashCode();
    }
}
