using System.Windows;

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
