using System.Windows;

namespace WinTabber.UI.Common.Hints;
public class DecoratedElementInfo
{
    public required string HintText { get; init; }

    public required FrameworkElement Element { get; init; }
}
