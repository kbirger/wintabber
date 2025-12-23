using System.Windows;
using WinTabberUI.Hints;

namespace WinTabberUI.Behaviors;
public interface IHintBehaviorKernel
{
    void Attach(FrameworkElement frameworkElement);
    void AttachChildren(IReadOnlyList<DependencyObject> childElements);
    void Detach(FrameworkElement frameworkElement);
    IReadOnlyList<FrameworkElement> GetAttachableElements(FrameworkElement rootElement);
    IEnumerable<DecoratedElementInfo> GetHints(IEnumerable<FrameworkElement> frameworkElements);
}