using System.Windows;

namespace WinTabber.UI.Common.Hints;

public interface IHintsProvider
{
    IEnumerable<DecoratedElementInfo> GetHints(IEnumerable<FrameworkElement> frameworkElements);
}