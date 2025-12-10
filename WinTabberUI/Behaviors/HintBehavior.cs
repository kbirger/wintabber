using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Xml.Linq;
using WinTabberUI.Services;

namespace WinTabberUI.Behaviors
{
    public class HintBehavior : Microsoft.Xaml.Behaviors.Behavior<System.Windows.FrameworkElement>
    {
        public static readonly DependencyProperty HintTextProperty =
            DependencyProperty.Register(
                nameof(HintText),
                typeof(string),
                typeof(HintBehavior),
                new PropertyMetadata(null));

        public static readonly DependencyProperty AdornerLayerAdornedElementsProperty =
                        DependencyProperty.RegisterAttached(
                "AdornerLayerAdornedElements",
                typeof(List<FrameworkElement>),
                typeof(HintBehavior),
                new PropertyMetadata(new List<FrameworkElement>()));
        private IHintBehaviorKernel _kernel;

        public string? HintText
        {
            get => (string?)GetValue(HintTextProperty);
            set => SetValue(HintTextProperty, value);
        }

        public static List<FrameworkElement> GetAttachedElements(DependencyObject obj) => (List<FrameworkElement>)obj.GetValue(AdornerLayerAdornedElementsProperty);

        public  IReadOnlyList<FrameworkElement> GetAdornedElements(DependencyObject obj) => _kernel.GetAttachableElements(AssociatedObject);
        public static void SetAttachAdorner(DependencyObject obj, List<FrameworkElement> value) => obj.SetValue(AdornerLayerAdornedElementsProperty, value);

        public static void AddAdornedElementToLayer(AdornerLayer adornerLayer, FrameworkElement element)
        {
            var list = GetAttachedElements(adornerLayer);
            if (!list.Contains(element))
            {
                list.Add(element);
                SetAttachAdorner(adornerLayer, list);
            }
        }

        private static void RemoveAdornedElement(DependencyObject obj, FrameworkElement element)
        {
            var list = GetAttachedElements(obj);
            if (list.Contains(element))
            {
                list.Remove(element);
                SetAttachAdorner(obj, list);
            }
        }

        private HintAdorner? _adorner;

        [MemberNotNullWhen(true, nameof(HintText))]
        private bool HasHintText => !string.IsNullOrEmpty(HintText);

        override protected void OnAttached()
        {
            base.OnAttached();
            _kernel = CreateKernel(AssociatedObject);

            if (HasHintText)
            {
                AttachToFrameworkElement(AssociatedObject);
            }
            else
            {
                DetachFromFrameworkElement(AssociatedObject);
            }
        }

        private void DetachFromFrameworkElement(FrameworkElement elem)
        {
            AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(elem);
            DetachTriggerKeyListener(elem);
            //DetachDeactivateListener(elem);
            RemoveAdornedElement(adornerLayer, elem);
        }

        private void AttachToFrameworkElement(FrameworkElement elem)
        {
            AttachAdorner(elem);
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
            AttachTriggerKeyListener(elem);
            var childElements = _kernel.GetAttachableElements(elem);

            _kernel.AttachChildren(childElements);

            //AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(elem);

            //AddAdornedElementToLayer(adornerLayer, elem);

        }

        private static void OnTriggerKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if(!(sender is FrameworkElement elem))
            {
                return;
            }

            if(e.Key == Key.LeftAlt || e.SystemKey == Key.LeftAlt)
            {
                ShowHints(elem);
                e.Handled = true;
            }
            else if(e.Key == Key.Escape || e.SystemKey == Key.Escape)
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
            var elements = HintBehavior.GetAttachedElements(rootElement).Where(element => element.IsLoaded);
            var key = e.SystemKey != Key.None ? e.SystemKey : e.Key;
            var text = key.ToString();
            foreach (var elem in elements)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(elem);
                var hintBehavior = HintBehavior.GetHintBehavior(elem);

                if (hintBehavior is null)
                {
                    continue;
                }

                var hint = hintBehavior.HintText;

                if (hint == text)
                {
                    var peer = FrameworkElementAutomationPeer.FromElement(elem);
                    if(peer is null)
                    {
                        if(Debugger.IsAttached)
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
                        prov?.Expand();
                        HideHints(rootElement);

                        ShowHints(comboBox);
                    }
                    else if (elem is ListBox listBox)
                    {
                        //ListBoxAutomationPeer peer = new ListBoxAutomationPeer(listBox);
                        peer.SetFocus();
                        HideHints(rootElement);
                    }
                    e.Handled = true;
                    break;
                }
            }

            return handled;
        }

        private static void HideHints(FrameworkElement rootElement)
        {
            var elements = HintBehavior.GetAttachedElements(rootElement).Where(element => element.IsLoaded);

            foreach(var elem in elements)
            {
                var hintBehavior = HintBehavior.GetHintBehavior(elem);
                hintBehavior?.DetachAdorner();
            }
        }

        private static void ShowHints(object sender)
        {
            var scope = (FrameworkElement)sender;

            var elements = HintBehavior.GetAttachedElements(scope).Where(element => element.IsLoaded);

            foreach (var elem in elements)
            {
                HintBehavior? hintBehavior = GetHintBehavior(elem);
                if (hintBehavior is null)
                {
                    continue;
                }
                var hint = hintBehavior.HintText;
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    hintBehavior.AttachAdorner();
                }
            }
        }

        private void DetachAdorner()
        {
            if(_adorner is not null)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(AssociatedObject);
                adornerLayer.Remove(_adorner);
                _adorner = null;
            }
        }

        private void AttachAdorner()
        {
            if(_adorner is null && HasHintText)
            {
                _adorner = new HintAdorner(AssociatedObject)
                { 
                    HintText = HintText 
                };
                var adornerLayer = AdornerLayer.GetAdornerLayer(AssociatedObject);
                adornerLayer.Add(_adorner);
            }
        }
        //private static void AttachExecuteKeyListener(FrameworkElement scope)
        //{
        //    scope.PreviewKeyDown += RootElement_PreviewKeyDown;
        //}

        //private static void DetachExecuteKeyListener(FrameworkElement scope)
        //{
        //    scope.PreviewKeyDown -= RootElement_PreviewKeyDown;
        //}

        public static HintBehavior? GetHintBehavior(FrameworkElement elem)
        {
            var behaviors = Interaction.GetBehaviors(elem);
            var hintBehavior = behaviors.OfType<HintBehavior>().FirstOrDefault();
            return hintBehavior;
        }

        private static void AttachTriggerKeyListener(FrameworkElement element)
        {
            Window.GetWindow(element).PreviewKeyDown += OnTriggerKeyDown;
        }
        private static void DetachTriggerKeyListener(FrameworkElement element)
        {
            Window.GetWindow(element).PreviewKeyDown -= OnTriggerKeyDown;
        }
        private void AssociatedObject_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                element.Loaded -= AssociatedObject_Loaded;
                AttachAdorner(element);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            DetachFromFrameworkElement(AssociatedObject);
        }

        private static void RootElement_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!(sender is FrameworkElement rootElement))
            {
                return;
            }

            if (e.SystemKey == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Escape)
            {
                HideHints(rootElement);
                e.Handled = true;
            }
            else
            {
                bool handled = false;
                var elements = HintBehavior.GetAttachedElements(rootElement).Where(element => element.IsLoaded);
                var key = e.SystemKey != System.Windows.Input.Key.None ? e.SystemKey : e.Key;
                var text = key.ToString();
                foreach (var elem in elements)
                {
                    var adornerLayer = AdornerLayer.GetAdornerLayer(elem);
                    var behaviors = Interaction.GetBehaviors(elem);
                    var hintBehavior = behaviors.OfType<HintBehavior>().FirstOrDefault();

                    if (hintBehavior is null)
                    {
                        continue;
                    }

                    var hint = hintBehavior.HintText;

                    if (hint == text)
                    {
                        var peer = FrameworkElementAutomationPeer.FromElement(elem);
                        if (elem is Button btn)
                        {
                            //ButtonAutomationPeer peer = new ButtonAutomationPeer(btn);
                            IInvokeProvider invokeProv = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                            invokeProv?.Invoke();
                            HideHints(rootElement);

                        }
                        else if (elem is ComboBox comboBox)
                        {
                            //ComboBoxAutomationPeer peer = new ComboBoxAutomationPeer(comboBox);
                            IExpandCollapseProvider prov = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
                            prov?.Expand();
                            HideHints(rootElement);

                            ShowHints(comboBox);
                        }
                        else if (elem is ListBox listBox)
                        {
                            //ListBoxAutomationPeer peer = new ListBoxAutomationPeer(listBox);
                            peer.SetFocus();
                            HideHints(rootElement);

                        }
                        e.Handled = true;
                        break;
                    }


                }
            }
        }
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
