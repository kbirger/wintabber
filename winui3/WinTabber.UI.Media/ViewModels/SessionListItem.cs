using Microsoft.UI.Xaml.Media.Imaging;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.ViewModels;

public class SessionListItem : ReactiveObject, IEquatable<SessionListItem>
{
    public SessionListItem(AggregateSession session)
    {
        Name = session.App.Name;
        _icon = session.App.Icon
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SelectMany(ToImageSourceAsync)
            .ToProperty(this, vm => vm.Icon, scheduler: RxSchedulers.MainThreadScheduler);
        Aumid = session.MediaSession.SourceAppUserModelId;
        Session = session;

        _icon.ThrownExceptions.Subscribe(ex =>
        {
            Debug.WriteLine("Error getting session app icon {0}", ex);
        });
    }

    /// <summary>
    /// Converts the framework-neutral <see cref="System.Drawing.Bitmap"/> the shell app
    /// repository reports into a WinUI 3-bindable <see cref="BitmapImage"/>. The WPF
    /// SessionListItem decodes the same <c>Icon</c> observable into its own <c>ImageSource</c>
    /// instead, via an HBITMAP conversion this project has no reason to duplicate.
    /// </summary>
    /// <remarks>
    /// The repository caches one <see cref="System.Drawing.Bitmap"/> per AUMID and replays it to
    /// every subscriber, so two <see cref="SessionListItem"/>s for the same app share the same
    /// instance here. <c>Bitmap.Save</c> is not documented safe under real concurrent access, but
    /// nothing here is concurrent: <c>ObserveOn(RxSchedulers.MainThreadScheduler)</c> runs every
    /// call to this method on the single WinUI 3 dispatcher thread, and <c>Save</c> itself never
    /// awaits, so one call always finishes before the next can start.
    /// </remarks>
    private static async Task<BitmapImage?> ToImageSourceAsync(System.Drawing.Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        await image.SetSourceAsync(stream.AsRandomAccessStream());
        return image;
    }

    public AggregateSession Session { get; init; }
    private readonly ObservableAsPropertyHelper<BitmapImage?> _icon;
    public string Name { get; init; }
    public BitmapImage? Icon => _icon.Value;
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
