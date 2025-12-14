using System.Windows;

namespace WinTabberUI.Behaviors;
public interface IHintBehaviorKernel
{
    void Attach(FrameworkElement frameworkElement);
    void AttachChildren(IReadOnlyList<DependencyObject> childElements);
    void Detach(FrameworkElement frameworkElement);
    IReadOnlyList<FrameworkElement> GetAttachableElements(FrameworkElement rootElement);
}