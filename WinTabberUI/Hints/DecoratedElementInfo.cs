using System.Windows;

namespace WinTabberUI.Hints;
public class DecoratedElementInfo
{
    public required string HintText { get; init; }

    public required FrameworkElement Element { get; init; }
}
