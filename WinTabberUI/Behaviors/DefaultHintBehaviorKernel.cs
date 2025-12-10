using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WinTabberUI.Behaviors;
public class DefaultHintBehaviorKernel : IHintBehaviorKernel
{
    public IReadOnlyList<FrameworkElement> GetAttachableElements(FrameworkElement rootElement)
    {
        return HintBehavior.GetAttachedElements(rootElement);
    }

    public void AttachChildren(IReadOnlyList<DependencyObject> childElements)
    {
        
    }
}
