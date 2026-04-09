using System.Windows;

namespace WinTabberUI.Hints;
public interface IDecoratedElementProvider
{
    public IEnumerable<DecoratedElementInfo> GetDecoratedElements(FrameworkElement element);
}
