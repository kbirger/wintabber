using System.Windows;

namespace WinTabberUI.Behaviors;
public interface IHintBehaviorKernel
{
    void AttachChildren(IReadOnlyList<DependencyObject> childElements);
    IReadOnlyList<FrameworkElement> GetAttachableElements(FrameworkElement rootElement);
}