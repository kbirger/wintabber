using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms.Design.Behavior;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using WinTabberUI.Services;

namespace WinTabberUI.Behaviors
{
    public class HintBehavior : Behavior<FrameworkElement>
    {
        private class HintItem
        {
            public required FrameworkElement Element { get; init; }
            public required IHintBehaviorKernel Kernel { get; init; }
        }
        public static readonly DependencyProperty HintTextProperty =
            DependencyProperty.RegisterAttached(
                "HintText",
                typeof(string),
                typeof(FrameworkElement),
                new PropertyMetadata(null, OnHintTextChanged));

        internal static readonly DependencyProperty HintAdornerProperty =
            DependencyProperty.RegisterAttached(
                "HintAdorner",
                typeof(HintAdorner),
                typeof(FrameworkElement),
                new PropertyMetadata(null, OnHintsShownChanged));


        internal static HintAdorner? GetHintAdorner(FrameworkElement element)
        {
            return element.GetValue(HintAdornerProperty) as HintAdorner;
        }

        internal static void SetHintAdorner(FrameworkElement element, HintAdorner? value)
        {
            element.SetValue(HintAdornerProperty, value);
        }

        private static HintBehavior? GetHintBehavior(FrameworkElement elem)
        {
            return Interaction.GetBehaviors(elem).OfType<HintBehavior>().FirstOrDefault();
        }
        public static readonly DependencyProperty AreHintsShownProperty =
             DependencyProperty.RegisterAttached(
                nameof(AreHintsShown),
                typeof(bool),
                typeof(HintBehavior),
                new PropertyMetadata(false, OnHintsShownChanged));
        
        public bool AreHintsShown
        {
            get => (bool)GetValue(AreHintsShownProperty);
            set => SetValue(AreHintsShownProperty, value);
        }
        private static void OnHintsShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }

        private static void OnHintTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(d))
            {
                return;
            }
            if (!(d is FrameworkElement elem))
            {
                return;
            }

            var window = Window.GetWindow(d);
            var behavior = HintBehavior.GetHintBehavior(window);
            if (behavior is not null)
            {
                if (e.NewValue is string newValue)
                {

                    if (!string.IsNullOrWhiteSpace(newValue))
                    {
                        behavior.RegisterHint(elem, newValue);
                    }
                    else if (e.OldValue is string oldValue)
                    {
                        behavior.UnregisterHint(oldValue);
                    }
                }
            }
        }

        private Dictionary<string, HintItem> _hints = new Dictionary<string, HintItem>();

        private void UnregisterHint(string hint)
        {
            _hints.Remove(hint);
        }

        private void RegisterHint(FrameworkElement elem, string hint)
        {
            _hints[hint] = new HintItem
            {
                Element = elem,
                Kernel = CreateKernel(elem)
            };
        }

        public static readonly DependencyProperty AdornerLayerAdornedElementsProperty =
                        DependencyProperty.RegisterAttached(
                "AdornerLayerAdornedElements",
                typeof(List<FrameworkElement>),
                typeof(HintBehavior),
                new PropertyMetadata(new List<FrameworkElement>()));
        private IHintBehaviorKernel _kernel;

        //public static string? HintText
        //{
        //    get => (string?)HintBehavior.GetValue(HintTextProperty);
        //    set => SetValue(HintTextProperty, value);
        //}

        public static string? GetHintText(DependencyObject obj)
        {
            return obj.GetValue(HintTextProperty) as string;
        }

        public static void SetHintText(DependencyObject obj, string? value)
        {
            obj.SetValue(HintTextProperty, value);
        }

        //public static List<FrameworkElement> GetAttachedElements(DependencyObject obj) => (List<FrameworkElement>)obj.GetValue(AdornerLayerAdornedElementsProperty);
        public static IEnumerable<FrameworkElement> GetAttachedElements(FrameworkElement obj)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(obj);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is FrameworkElement elem)
                {
                    if (GetHintText(child) is not null)
                    {
                        yield return elem;

                    }
                    foreach (var grandChild in GetAttachedElements(elem))
                    {
                        yield return grandChild;
                    }
                }
            }
        }

        public IReadOnlyList<FrameworkElement> GetAdornedElements(DependencyObject obj) => _kernel.GetAttachableElements(AssociatedObject);
        public static void SetAttachAdorner(DependencyObject obj, List<FrameworkElement> value) => obj.SetValue(AdornerLayerAdornedElementsProperty, value);

        public static void AddAdornedElementToLayer(AdornerLayer adornerLayer, FrameworkElement element)
        {
            //var list = GetAttachedElements(adornerLayer);
            //if (!list.Contains(element))
            //{
            //    list.Add(element);
            //    SetAttachAdorner(adornerLayer, list);
            //}
        }

        private static void RemoveAdornedElement(DependencyObject obj, FrameworkElement element)
        {
            //var list = GetAttachedElements(obj);
            //if (list.Contains(element))
            //{
            //    list.Remove(element);
            //    SetAttachAdorner(obj, list);
            //}
        }

        private HintAdorner? _adorner;

        override protected void OnAttached()
        {
            base.OnAttached();
            _kernel = CreateKernel(AssociatedObject);

            AttachToFrameworkElement(AssociatedObject);
        }

        private void DetachFromFrameworkElement(FrameworkElement elem)
        {
            AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(elem);
            DetachTriggerKeyListener(elem);
            _kernel.Detach(elem);
            //DetachDeactivateListener(elem);
            RemoveAdornedElement(adornerLayer, elem);
        }

        private void AttachToFrameworkElement(FrameworkElement elem)
        {
            AttachTriggerKeyListener(elem);
            var kernel = CreateKernel(elem);

            var elements = kernel.GetAttachableElements(elem);
            kernel.Attach(elem);
            //foreach(var element in elements.Where(child => child is ItemsControl))
            //{
            //    var behaviors = Interaction.GetBehaviors(element);
            //    if (!behaviors.OfType<HintBehavior>().Any())
            //    {
            //        behaviors.Add(new HintBehavior());
            //    }
            //}
            //AttachAdorner(elem);
            //if (elem.IsLoaded)
            //{
            //    //HintAdornerService.ShowHint(AssociatedObject, HintText);
            //}
            //else
            //{
            //    elem.Loaded += AssociatedObject_Loaded;
            //}
        }

        private static IHintBehaviorKernel CreateKernel(FrameworkElement rootElement)
        {
            return rootElement switch
            {
                ItemsControl itemsControl => new ItemsControlHintBehaviorKernel(),
                FrameworkElement _ => new DefaultHintBehaviorKernel(),
                _ => throw new NotSupportedException()
            };
        }

        private void AttachAdorner(FrameworkElement elem)
        {
            var childElements = _kernel.GetAttachableElements(elem);

            _kernel.AttachChildren(childElements);

            //AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(elem);

            //AddAdornedElementToLayer(adornerLayer, elem);

        }

        private static void OnTriggerKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!(sender is FrameworkElement elem))
            {
                return;
            }

            if (e.Key == Key.LeftAlt || e.SystemKey == Key.LeftAlt)
            {
                ShowHints(elem);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape || e.SystemKey == Key.Escape)
            {
                HideHints(elem);
                e.Handled = true;
            }
            else
            {
                e.Handled = ProcessKey(elem, e);
            }
        }

        private static bool ProcessKey(FrameworkElement rootElement, KeyEventArgs e)
        {
            bool handled = false;
            var behavior = HintBehavior.GetHintBehavior(rootElement);
            if(behavior is null || !behavior.AreHintsShown)
            {
                return false;
            }
            var elements = behavior._kernel.GetAttachableElements(rootElement);
            //var elements =  HintBehavior.GetAttachedElements(rootElement).Where(element => element.IsLoaded);
            var key = e.SystemKey != Key.None ? e.SystemKey : e.Key;
            var text = GetText(key);
            foreach (var elem in elements)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(elem);


                var hint = GetHintText(elem);

                if (hint == text)
                {
                    var peer = FrameworkElementAutomationPeer.CreatePeerForElement(elem);
                    if (peer is null)
                    {
                        if (Debugger.IsAttached)
                        {
                            Debugger.Break();
                        }
                        return false;
                    }
                    if (elem is Button btn)
                    {
                        //ButtonAutomationPeer peer = new ButtonAutomationPeer(btn);
                        IInvokeProvider? invokeProv = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                        invokeProv?.Invoke();
                        HideHints(rootElement);

                    }
                    else if (elem is ComboBox comboBox)
                    {
                        //ComboBoxAutomationPeer peer = new ComboBoxAutomationPeer(comboBox);
                        IExpandCollapseProvider? prov = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
                        peer.SetFocus();
                        elem.Focus();
                        prov?.Expand();
                        HideHints(rootElement);

                        //ShowHints(comboBox);
                    }
                    else if (elem is ListBox listBox)
                    {
                        //ListBoxAutomationPeer peer = new ListBoxAutomationPeer(listBox);
                        peer.SetFocus();
                        elem.Focus();
                        HideHints(rootElement);
                    }
                    else if(elem is ComboBoxItem cbi)
                    {
                        cbi.IsSelected = true;
                        //var prov = peer.GetPattern(PatternInterface.SelectionItem);
                        IExpandCollapseProvider? prov = FrameworkElementAutomationPeer.CreatePeerForElement(rootElement)
                            .GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
                        prov?.Collapse();
                        //var i = new ListBoxItemAutomationPeer(cbi, r);
                        //(i as ISelectionItemProvider).Select();
                        //var p = peer.GetPattern(PatternInterface.SelectionItem);
                        HideHints(rootElement);    
                        //peer.par
                        //peer = new ListBoxItemAutomationPeer(elem, new )
                    }

                    if (Interaction.GetBehaviors(elem).OfType<HintBehavior>().Any())
                    {
                        ShowHints(elem);
                    }
                    e.Handled = true;
                    break;
                }
            }

            return handled;
        }

        private static string GetText(Key key)
        {
            // Letters A–Z
            if (key >= Key.A && key <= Key.Z)
                return key.ToString(); // already "A"…"Z"

            // Top-row digits D0–D9
            if (key >= Key.D0 && key <= Key.D9)
                return ((int)(key - Key.D0)).ToString();

            // Numpad digits NumPad0–NumPad9
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return ((int)(key - Key.NumPad0)).ToString();

            return string.Empty;
        }

        private static void HideHints(FrameworkElement rootElement)
        {
            var behavior = GetHintBehavior(rootElement);
            if (behavior is null || !behavior.AreHintsShown)
            {
                return;
            }

            //var elements = GetAttachedElements(rootElement).Where(element => element.IsLoaded);
            var elements = behavior._kernel.GetAttachableElements(rootElement);
            foreach (var elem in elements)
            {
                var layer = AdornerLayer.GetAdornerLayer(elem);
                if (layer is not null)
                {
                    var adorner = GetHintAdorner(elem);
                    if(adorner is not null)
                    {
                        layer.Remove(adorner);
                        SetHintAdorner(elem, null);
                    }
                }
            }

            behavior.AreHintsShown = false;

        }

        private static void ShowHints(FrameworkElement sender)
        {
            var behavior = GetHintBehavior(sender);
            if(behavior is null || behavior.AreHintsShown)
            {
                return;
            }
            //var elements = GetAttachedElements(scope).Where(element => element.IsLoaded);

            var elements = behavior._kernel.GetAttachableElements(sender);
            //kernel.AttachChildren(elements);
            foreach (var elem in elements)
            {
                var hint = GetHintText(elem);
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    AttachAdornerToElement(elem);
                }
            }

            behavior.AreHintsShown = true;
        }

        private void DetachAdorner()
        {
            if (_adorner is not null)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(AssociatedObject);
                adornerLayer.Remove(_adorner);
                _adorner = null;
            }
        }

        private static void AttachAdornerToElement(FrameworkElement elem)
        {
            HintAdorner? adorner = GetHintAdorner(elem) ?? 
                new HintAdorner(elem)
                {
                    HintText = GetHintText(elem)!
                };
            SetHintAdorner(elem, adorner);
            var adornerLayer = AdornerLayer.GetAdornerLayer(elem);
            adornerLayer.Add(adorner);
        }
        //private static void AttachExecuteKeyListener(FrameworkElement scope)
        //{
        //    scope.PreviewKeyDown += RootElement_PreviewKeyDown;
        //}

        //private static void DetachExecuteKeyListener(FrameworkElement scope)
        //{
        //    scope.PreviewKeyDown -= RootElement_PreviewKeyDown;
        //}

        private static void AttachTriggerKeyListener(FrameworkElement window)
        {
            window.PreviewKeyDown += OnTriggerKeyDown;
        }
        private static void DetachTriggerKeyListener(FrameworkElement window)
        {
            window.PreviewKeyDown -= OnTriggerKeyDown;
        }
        private void AssociatedObject_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                //element.Loaded -= AssociatedObject_Loaded;
                //AttachAdorner(element);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            DetachFromFrameworkElement(AssociatedObject);
        }

        //private static void RootElement_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        //{
        //    if (!(sender is FrameworkElement rootElement))
        //    {
        //        return;
        //    }

        //    if (e.SystemKey == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Escape)
        //    {
        //        HideHints(rootElement);
        //        e.Handled = true;
        //    }
        //    else
        //    {
        //        bool handled = false;
        //        var elements = HintBehavior.GetAttachedElements(rootElement).Where(element => element.IsLoaded);
        //        var key = e.SystemKey != System.Windows.Input.Key.None ? e.SystemKey : e.Key;
        //        var text = key.ToString();
        //        foreach (var elem in elements)
        //        {
        //            var adornerLayer = AdornerLayer.GetAdornerLayer(elem);
        //            var behaviors = Interaction.GetBehaviors(elem);
        //            var hintBehavior = behaviors.OfType<HintBehavior>().FirstOrDefault();

        //            if (hintBehavior is null)
        //            {
        //                continue;
        //            }

        //            var hint = hintBehavior.HintText;

        //            if (hint == text)
        //            {
        //                var peer = FrameworkElementAutomationPeer.FromElement(elem);
        //                if (elem is Button btn)
        //                {
        //                    //ButtonAutomationPeer peer = new ButtonAutomationPeer(btn);
        //                    IInvokeProvider invokeProv = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        //                    invokeProv?.Invoke();
        //                    HideHints(rootElement);

        //                }
        //                else if (elem is ComboBox comboBox)
        //                {
        //                    //ComboBoxAutomationPeer peer = new ComboBoxAutomationPeer(comboBox);
        //                    IExpandCollapseProvider prov = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
        //                    prov?.Expand();
        //                    HideHints(rootElement);

        //                    ShowHints(comboBox);
        //                }
        //                else if (elem is ListBox listBox)
        //                {
        //                    //ListBoxAutomationPeer peer = new ListBoxAutomationPeer(listBox);
        //                    peer.SetFocus();
        //                    HideHints(rootElement);

        //                }
        //                e.Handled = true;
        //                break;
        //            }


        //        }
        //    }
        //}
        //public void Activate(FrameworkElement scope)
        //{
        //    var elements = HintBehavior.GetAdornedElements(scope).Where(element => element.IsLoaded);
        //    AttachDeactivateListener(scope);

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
        //        if (!string.IsNullOrWhiteSpace(hint))
        //        {

        //            var adorner = new HintAdorner(elem)
        //            {
        //                HintText = hint
        //            };

        //            adornerLayer.Add(adorner);

        //        }
        //    }
        //}

        //private static void AttachHideHintsKeyListener(FrameworkElement scope)
        //{
        //    scope.PreviewKeyDown += OnDeactivateKeyDown;

        //}

        //private static void DetachDeactivateListener(FrameworkElement scope)
        //{
        //    scope.PreviewKeyDown -= OnDeactivateKeyDown;

        //}

        //private static void OnDeactivateKeyDown(object sender, KeyEventArgs e)
        //{
        //    if(sender is FrameworkElement element)
        //    {

        //        DetachDeactivateListener(element);
        //        e.Handled = true;
        //    }
        //}
    }
}
