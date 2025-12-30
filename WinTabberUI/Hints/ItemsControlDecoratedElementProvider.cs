using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinTabberUI.Behaviors;

namespace WinTabberUI.Hints;

public class ItemsControlDecoratedElementProvider : IDecoratedElementProvider
{
    public IEnumerable<DecoratedElementInfo> GetDecoratedElements(FrameworkElement element)
    {
        if (element is ItemsControl itemsControl)
        {

            var elements = HintBehavior.GetAttachedElements(element).Where(child => child.IsLoaded);

            return elements.Select(child => new DecoratedElementInfo
            {
                Element = child,
                HintText = HintBehavior.GetHintText(child)!
            });
        }

        return Enumerable.Empty<DecoratedElementInfo>();
    }
}
