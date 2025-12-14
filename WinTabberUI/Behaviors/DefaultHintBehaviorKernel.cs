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
        //return [];
        return HintBehavior.GetAttachedElements(rootElement).Where(elem => elem.IsLoaded).ToList();
    }

    public void AttachChildren(IReadOnlyList<DependencyObject> childElements)
    {
        
    }

    public void Attach(FrameworkElement frameworkElement)
    {
    }

    public void Detach(FrameworkElement frameworkElement)
    {
    }
}
