using System.Windows;

namespace WinTabber.UI.Common.Hints;
public interface IDecoratedElementProvider
{
    public IEnumerable<DecoratedElementInfo> GetDecoratedElements(FrameworkElement element);
}
