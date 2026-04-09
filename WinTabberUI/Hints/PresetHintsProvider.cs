using System.Windows;
using WinTabberUI.Behaviors;

namespace WinTabberUI.Hints;

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
