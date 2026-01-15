using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinTabberUI.Chrome;

namespace WinTabberUI.Views;
/// <summary>
/// Interaction logic for FadingWindow.xaml
/// </summary>
public partial class FadingWindow : Window
{
    public FadingWindow()
    {
        InitializeComponent();
        Loaded += FadingWindow_Loaded;
    }

    private void FadingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Resources["ScaleTransform"] is ScaleTransform scale)
        {
            //scale.ScaleX = Width;
            //scale.ScaleY = Height;
        }
    }

    public ImageSource ImageSource
    {
        get => DataContext as ImageSource;
    }


    public static DependencyProperty OpacityStartProperty = DependencyProperty.Register(
        nameof(OpacityStart),
        typeof(double),
        typeof(FadingWindow),
        new PropertyMetadata(0.0));

    public double OpacityStart
    {
        get => (double)GetValue(OpacityStartProperty);
        set => SetValue(OpacityStartProperty, value);
    }

    public static DependencyProperty OpacityEndProperty = DependencyProperty.Register(
            nameof(OpacityEnd),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(1.0));

    public double OpacityEnd
    {
        get => (double)GetValue(OpacityEndProperty);
        set => SetValue(OpacityEndProperty, value);
    }

    public static DependencyProperty AnimationStartWidthProperty = DependencyProperty.Register(
            nameof(AnimationStartWidth),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(0.0));

    public double AnimationStartWidth
    {
        get => (double)GetValue(AnimationStartWidthProperty);
        set => SetValue(AnimationStartWidthProperty, value);
    }

    public static DependencyProperty AnimationStartHeightProperty = DependencyProperty.Register(
            nameof(AnimationStartHeight),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(0.0));

    public double AnimationStartHeight
    {
        get => (double)GetValue(AnimationStartHeightProperty);
        set => SetValue(AnimationStartHeightProperty, value);
    }

    public static DependencyProperty AnimationDurationProperty = DependencyProperty.Register(
            nameof(AnimationDuration),
            typeof(Duration),
            typeof(FadingWindow),
            new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(100))));

    public Duration AnimationDuration
    {
        get => (Duration)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public static DependencyProperty AnimationEndWidthProperty = DependencyProperty.Register(
            nameof(AnimationEndWidth),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(0.0));

    public double AnimationEndWidth
    {
        get => (double)GetValue(AnimationEndWidthProperty);
        set => SetValue(AnimationEndWidthProperty, value);
    }

    public static DependencyProperty AnimationEndHeightProperty = DependencyProperty.Register(
            nameof(AnimationEndHeight),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(0.0));

    public double AnimationEndHeight
    {
        get => (double)GetValue(AnimationEndHeightProperty);
        set => SetValue(AnimationEndHeightProperty, value);
    }   

    private Action? OnAnimationEnd { get; set; }

    public void FadeOut()
    {
        AnimationStartHeight = ActualHeight;
        AnimationStartWidth = ActualWidth;
        AnimationEndHeight = ActualHeight * .8;
        AnimationEndWidth = ActualWidth *.8;
        AnimationDuration = TimeSpan.FromMilliseconds(100);
        OpacityStart = 1.0;
        OpacityEnd = 0.0;
        OnAnimationEnd = Close;
        if (Resources["FadeOut"] is Storyboard sb)
        {
            sb.Begin();
        }
    }

    public void FadeIn(Window target)
    {
        Show();

        //var hwndSource = (HwndSource)PresentationSource.FromVisual(target);
        //CloakHelper.Cloak(hwndSource.Handle);
        AnimationStartHeight = ActualHeight * .2;
        AnimationStartWidth = ActualWidth * .8;
        AnimationEndHeight = ActualHeight;
        AnimationEndWidth = ActualWidth;
        AnimationDuration = TimeSpan.FromMilliseconds(1000);
        OpacityStart = 0.0;
        OpacityEnd = 1.0;
        OnAnimationEnd = () =>
        {
            Close();
            target.Show();
        };

        ;
        if (Resources["FadeOut"] is Storyboard sb)
        {
            sb.Begin();
        }
    }

    private void FadeOutStoryboard_Completed(object sender, EventArgs e)
    {
        OnAnimationEnd?.Invoke();
    }
}
