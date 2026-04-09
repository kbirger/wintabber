using System.Windows;
using WinTabber.UI.Common.Behaviors;

namespace WinTabber.UI.Common.Hints;
public class DefaultDecoratedElementProvider : IDecoratedElementProvider
{
    public IEnumerable<DecoratedElementInfo> GetDecoratedElements(FrameworkElement element)
    {
        var elements = HintBehavior.GetAttachedElements(element).Where(child => child.IsLoaded);

        return elements.Select(child => new DecoratedElementInfo
        {
            Element = child,
            HintText = HintBehavior.GetHintText(child)!
        });
    }
}
