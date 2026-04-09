using System.Windows;
using WinTabber.UI.Common.Behaviors;

namespace WinTabber.UI.Common.Hints;

public class PresetHintsProvider : IHintsProvider
{
    public IEnumerable<DecoratedElementInfo> GetHints(IEnumerable<FrameworkElement> frameworkElements)
    {
        return frameworkElements
            .Select(element => (element, HintBehavior.GetHintText(element)))
            .Where(item => item.Item2 != null)
            .Select(item => new DecoratedElementInfo
            {
                Element = item.Item1,
                HintText = item.Item2!
            });
    }
}
