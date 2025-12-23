using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WinTabberUI.Behaviors;

namespace WinTabberUI.Hints;

public class ItemsControlHintsProvider : IHintsProvider
{
    public IEnumerable<DecoratedElementInfo> GetHints(IEnumerable<FrameworkElement> frameworkElements)
    {
        return frameworkElements
            .Select((element, idx) => new DecoratedElementInfo
            {
                Element = element,
                HintText = (idx + 1).ToString()
            });
    }
}
