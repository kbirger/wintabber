namespace WinTabber.ViewModels.Models;

/// <summary>
/// A plain (X, Y) location, framework-agnostic so <see cref="WindowTileGrid"/> and
/// <see cref="WindowTileInfo"/> can be shared between the WPF and WinUI 3 apps' spatial-navigation
/// list views: neither System.Windows.Point (WPF, double-precision) nor Windows.Foundation.Point
/// (WinRT, float-precision) belongs in WinTabber.ViewModels, which must stay UI-framework-free.
/// </summary>
public readonly record struct TilePoint(double X, double Y);

/// <summary>
/// One tile's position and backing data within a <see cref="WindowTileGrid"/>.
/// <see cref="Container"/> is intentionally untyped: <see cref="WindowTileGrid"/> never reads it
/// back (only <see cref="Location"/>, <see cref="IsSelected"/>, <see cref="Index"/>, and
/// <see cref="WindowItem"/> drive grid construction and navigation), so each app's
/// SpatialNavigationListView stores whatever its own framework's container type is (WPF Visual,
/// WinUI 3 FrameworkElement) purely for the caller's own use.
/// </summary>
public class WindowTileInfo
{
    public required WindowItem WindowItem { get; init; }
    public required TilePoint Location { get; init; }
    public required object Container { get; init; }

    public required int Index { get; init; }

    public required bool IsSelected { get; set; }

    public override string ToString()
    {
        return $"{Index}: {Location.X}, {Location.Y}";
    }
}
