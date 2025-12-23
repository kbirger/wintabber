using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinTabberUI.Hints;

namespace WinTabberUI.Behaviors;
public class ItemsControlHintBehaviorKernel : IHintBehaviorKernel
{
    public void Attach(FrameworkElement frameworkElement)
    {
        if(frameworkElement is ItemsControl itemsControl)
        {
            itemsControl.ItemContainerGenerator.StatusChanged += ItemContainerGenerator_StatusChanged;
            itemsControl.ItemContainerGenerator.ItemsChanged += ItemContainerGenerator_ItemsChanged;
        }
    }

    private void ItemContainerGenerator_StatusChanged(object? sender, EventArgs e)
    {
        if (sender is ItemContainerGenerator generator && generator.Status == GeneratorStatus.ContainersGenerated)
        {
            List<DependencyObject> items = new();
            for (int i = 0; i < generator.Items.Count; i++)
            {
                items.Add(generator.ContainerFromIndex(i));
            }
            AttachChildren(items);
        }
    }

    public void Detach(FrameworkElement frameworkElement)
    {
        if (frameworkElement is ItemsControl itemsControl)
        {
            itemsControl.ItemContainerGenerator.StatusChanged -= ItemContainerGenerator_StatusChanged;

            itemsControl.ItemContainerGenerator.ItemsChanged -= ItemContainerGenerator_ItemsChanged;
        }
    }

    private void ItemContainerGenerator_ItemsChanged(object sender, System.Windows.Controls.Primitives.ItemsChangedEventArgs e)
    {
        if(sender is ItemContainerGenerator generator)
        {
            List<DependencyObject> items = new();
            for(int i = 0; i < generator.Items.Count; i++)
            {
                items.Add(generator.ContainerFromIndex(i));
            }
            //AttachChildren(items);
        }
    }

    public void AttachChildren(IReadOnlyList<DependencyObject> childElements)
    {
        foreach(var info in GetHints(childElements.OfType<FrameworkElement>()))
        {
            HintBehavior.SetHintText(info.Element, info.HintText);
            HintBehavior.SetHintPosition(info.Element, HintPosition.RightInset);
        }
    }

    public IReadOnlyList<FrameworkElement> GetAttachableElements(FrameworkElement rootElement)
    {
        if(rootElement is ItemsControl itemsControl)
        {
            List<FrameworkElement> items = new List<FrameworkElement>(itemsControl.Items.Count);
            for(int i = 0; i<itemsControl.Items.Count;i++)
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

    private IHintsProvider _hintsProvider = new ItemsControlHintsProvider();
    public IEnumerable<DecoratedElementInfo> GetHints(IEnumerable<FrameworkElement> frameworkElements)
    {
        return _hintsProvider.GetHints(frameworkElements);
    }
}
