using System.Windows;

namespace WinTabberUI.Hints;

public interface IHintsProvider
{
    IEnumerable<DecoratedElementInfo> GetHints(IEnumerable<FrameworkElement> frameworkElements);
}