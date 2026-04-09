using System.Windows;
using System.Windows.Shell;

namespace WinTabberUI.Chrome
{
    public class CustomChrome : WindowChrome
    {
        public static readonly DependencyProperty CustomChromeProperty = DependencyProperty.RegisterAttached(
           "WindowChrome",
           typeof(CustomChrome),
           typeof(CustomChrome),
           new PropertyMetadata(null, CustomChromeChanged));
        private static void CustomChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is CustomChrome customChrome && d is Window window)
            {
                window.ContentRendered += Window_ContentRendered;
                window.SetValue(WindowChrome.WindowChromeProperty, customChrome);
            }
        }

        private static void Window_ContentRendered(object? sender, EventArgs e)
        {
            if(sender is Window window)
            {
                if (window.Content is not CaptionButtons)
                {
                    var btns = new CaptionButtons()
                    {
                        Window = window
                    };
                    var originalContent = window.Content;

                    btns.NavView.Content = originalContent;
                    window.Content = btns;
                    window.ContentRendered -= Window_ContentRendered;
                }
            }
        }

        public static void SetCustomChrome(Window window, CustomChrome chrome)
        {
            window.SetValue(CustomChromeProperty, chrome);
        }

        public static CustomChrome GetCustomChrome(Window window)
        {
            return (CustomChrome)window.GetValue(CustomChromeProperty);
        }
    }
}
