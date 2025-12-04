using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
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

        private static readonly DependencyProperty AdornerLayerAdornedElementsProperty =
                        DependencyProperty.RegisterAttached(
                "AdornerLayerAdornedElements",
                typeof(List<FrameworkElement>),
                typeof(HintBehavior),
                new PropertyMetadata(new List<FrameworkElement>()));

        public string? HintText
        {
            get => (string?)GetValue(HintTextProperty);
            set => SetValue(HintTextProperty, value);
        }

        private static List<FrameworkElement> GetAttachAdorner(DependencyObject obj) => (List<FrameworkElement>)obj.GetValue(AdornerLayerAdornedElementsProperty);

        public static IReadOnlyList<FrameworkElement> GetAdornedElements(DependencyObject obj) => GetAttachAdorner(obj).AsReadOnly();
        private static void SetAttachAdorner(DependencyObject obj, List<FrameworkElement> value) => obj.SetValue(AdornerLayerAdornedElementsProperty, value);

        private static void AddAdornedElement(DependencyObject obj, FrameworkElement element)
        {
            var list = GetAttachAdorner(obj);
            if (!list.Contains(element))
            {
                list.Add(element);
                SetAttachAdorner(obj, list);
            }
        }

        private static void RemoveAdornedElement(DependencyObject obj, FrameworkElement element)
        {
            var list = GetAttachAdorner(obj);
            if (list.Contains(element))
            {
                list.Remove(element);
                SetAttachAdorner(obj, list);
            }
        }

        private bool HasHintText => !string.IsNullOrEmpty(HintText);

        override protected void OnAttached()
        {
            base.OnAttached();

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
            Window.GetWindow(elem).PreviewKeyDown -= Elem_PreviewKeyDown;
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

        private static void AttachAdorner(FrameworkElement elem)
        {
            AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(elem);
            Window.GetWindow(elem).PreviewKeyDown += Elem_PreviewKeyDown;

            AddAdornedElement(adornerLayer, elem);

        }

        private static void Elem_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if(e.Key == System.Windows.Input.Key.LeftAlt || e.SystemKey == System.Windows.Input.Key.LeftAlt)
            {
                HintService.ShowHints(sender as FrameworkElement);
                e.Handled = true;
            }
        }

        private static void AssociatedObject_Loaded(object sender, RoutedEventArgs e)
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
    }
}
