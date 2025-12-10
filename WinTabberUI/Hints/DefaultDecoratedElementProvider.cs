using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WinTabberUI.Behaviors;

namespace WinTabberUI.Hints;
public class DefaultDecoratedElementProvider : IDecoratedElementProvider
{
    public IEnumerable<DecoratedElementInfo> GetDecoratedElements(FrameworkElement element)
    {
        var elements = HintBehavior.GetAttachedElements(element).Where(child => child.IsLoaded);

        return elements.Select(child => new DecoratedElementInfo
        {
            Element = child,
            HintText = HintBehavior.GetHintBehavior(child)?.HintText
        });
    }
}
