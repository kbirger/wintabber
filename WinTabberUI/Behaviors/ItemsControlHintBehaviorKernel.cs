using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WinTabberUI.Behaviors;
public class ItemsControlHintBehaviorKernel : IHintBehaviorKernel
{
    public void AttachChildren(IReadOnlyList<DependencyObject> childElements)
    {
        for(var i = 0; i < childElements.Count; i++)
        {
            var child = childElements[i];
            var behavior = new HintBehavior
            {
                HintText = i.ToString()
            };
            var behaviors = Interaction.GetBehaviors(child);
            behaviors.Add(behavior);
        }
    }

    public IReadOnlyList<FrameworkElement> GetAttachableElements(FrameworkElement rootElement)
    {
        if(rootElement is ItemsControl itemsControl)
        {
            List<FrameworkElement> items = new List<FrameworkElement>(itemsControl.Items.Count);
            for(int i = 0; i<items.Count;i++)
            {
                if(itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement elem)
                {
                    items.Add(elem);

                }
            }

            return items.ToArray();
        }

        return [];
    }
}
