using System.Windows;
using System.Windows.Media;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

public class WindowTileInfo
{
    public required WindowItem WindowItem { get; init; }
    public required Point Location { get; init; }
    public required Visual Container { get; init; }

    public required int Index { get; init; }

    //public int Distance { get; set; }

    public required bool IsSelected { get; set; }

    public override string ToString()
    {
        return $"{Index}: {Location.X}, {Location.Y}";
    }
}
