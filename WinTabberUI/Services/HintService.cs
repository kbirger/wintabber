using System.Windows;
using System.Windows.Controls;
//using System.Windows.Forms;

namespace WinTabberUI.Services;

public static class HintService
{
    public static void ShowHints(FrameworkElement rootElement)
    {
        rootElement.PreviewKeyDown += RootElement_PreviewKeyDown;
        ApplyAdorners(rootElement);
    }

    private static void ApplyAdorners(FrameworkElement rootElement)
    {
        if(rootElement is ItemsControl itemsControl)
        {
            ApplyAdornersForItemsControl(itemsControl);
        }
        else
        {
            ApplyAdornersDefault(rootElement);
        }
            
    }

    private static void ApplyAdornersDefault(FrameworkElement rootElement)
    {
        //var elements = HintBehavior.GetAdornedElements(rootElement).Where(element => element.IsLoaded);


        //foreach (var elem in elements)
        //{
        //    var adornerLayer = AdornerLayer.GetAdornerLayer(elem);
        //    var behaviors = Interaction.GetBehaviors(elem);
        //    var hintBehavior = behaviors.OfType<HintBehavior>().FirstOrDefault();
        //    if (hintBehavior is null)
        //    {
        //        continue;
        //    }
        //    var hint = hintBehavior.HintText;
        //    if (!string.IsNullOrWhiteSpace(hint))
        //    {

        //        var adorner = new HintAdorner(elem)
        //        {
        //            HintText = hint
        //        };

        //        adornerLayer.Add(adorner);

        //    }
        //}
    }

    private static IEnumerable<FrameworkElement> ApplyAdornersForItemsControl(ItemsControl itemsControl)
    {
        for (int i = 0; i < itemsControl.Items.Count; i++)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement item)
            {
                yield return item;
            }
        }
    }

    public static void HideHints(FrameworkElement rootElement)
    {
        //rootElement.PreviewKeyDown -= RootElement_PreviewKeyDown;
        //var elements = HintBehavior.GetAdornedElements(rootElement).Where(element => element.IsLoaded);
        //foreach (var elem in elements)
        //{
        //    var adornerLayer = AdornerLayer.GetAdornerLayer(elem);

        //    var adorners = adornerLayer.GetAdorners(elem).OfType<HintAdorner>();

        //    foreach (var adorner in adorners)
        //    {
        //        adornerLayer.Remove(adorner);
        //    }
        //}
    }

    private static void RootElement_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        //if (!(sender is FrameworkElement rootElement))
        //{
        //    return;
        //}

        //if (e.SystemKey == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Escape)
        //{
        //    HideHints(rootElement);
        //    e.Handled = true;
        //}
        //else
        //{
        //    bool handled = false;
        //    var elements = HintBehavior.GetAdornedElements(rootElement).Where(element => element.IsLoaded);
        //    var key = e.SystemKey != System.Windows.Input.Key.None ? e.SystemKey : e.Key;
        //    var text = key.ToString();
        //    foreach (var elem in elements)
        //    {
        //        var adornerLayer = AdornerLayer.GetAdornerLayer(elem);
        //        var behaviors = Interaction.GetBehaviors(elem);
        //        var hintBehavior = behaviors.OfType<HintBehavior>().FirstOrDefault();

        //        if (hintBehavior is null)
        //        {
        //            continue;
        //        }

        //        var hint = hintBehavior.HintText;

        //        if (hint == text)
        //        {
        //            var peer = FrameworkElementAutomationPeer.FromElement(elem);
        //            if (elem is Button btn)
        //            {
        //                //ButtonAutomationPeer peer = new ButtonAutomationPeer(btn);
        //                IInvokeProvider invokeProv = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        //                invokeProv?.Invoke();
        //                HideHints(rootElement);

        //            }
        //            else if (elem is ComboBox comboBox)
        //            {
        //                //ComboBoxAutomationPeer peer = new ComboBoxAutomationPeer(comboBox);
        //                IExpandCollapseProvider prov = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
        //                prov?.Expand();
        //                HideHints(rootElement);

        //                ShowHints(comboBox);
        //            }
        //            else if (elem is ListBox listBox)
        //            {
        //                //ListBoxAutomationPeer peer = new ListBoxAutomationPeer(listBox);
        //                peer.SetFocus();
        //                HideHints(rootElement);

        //            }
        //            e.Handled = true;
        //            break;
        //        }


        //    }
        //}
    }
}
