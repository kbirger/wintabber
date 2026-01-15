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


    public static DependencyProperty AnimationLeftStartProperty = DependencyProperty.Register(
            nameof(AnimationLeftStart),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(0.0));

    public double AnimationLeftStart
    {
        get => (double)GetValue(AnimationLeftStartProperty);
        set => SetValue(AnimationLeftStartProperty, value);
    }

    public static DependencyProperty AnimationLeftEndProperty = DependencyProperty.Register(
            nameof(AnimationLeftEnd),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(0.0));

    public double AnimationLeftEnd
    {
        get => (double)GetValue(AnimationLeftEndProperty);
        set => SetValue(AnimationLeftEndProperty, value);
    }


    public static DependencyProperty AnimationTopStartProperty = DependencyProperty.Register(
        nameof(AnimationTopStart),
        typeof(double),
        typeof(FadingWindow),
        new PropertyMetadata(0.0));

    public double AnimationTopStart
    {
        get => (double)GetValue(AnimationTopStartProperty);
        set => SetValue(AnimationTopStartProperty, value);
    }

    public static DependencyProperty AnimationTopEndProperty = DependencyProperty.Register(
            nameof(AnimationTopEnd),
            typeof(double),
            typeof(FadingWindow),
            new PropertyMetadata(0.0));

    public double AnimationTopEnd
    {
        get => (double)GetValue(AnimationTopEndProperty);
        set => SetValue(AnimationTopEndProperty, value);
    }

    private Action? OnAnimationEnd { get; set; }

    public void FadeOut()
    {
        double scaleFactor = .3;
        AnimationStartHeight = ActualHeight;
        AnimationEndHeight = ActualHeight * scaleFactor;
        AnimationTopStart = Top;
        AnimationTopEnd = Top + AnimationStartHeight / 2 / 2;

        AnimationStartWidth = ActualWidth;
        AnimationEndWidth = ActualWidth * scaleFactor;
        AnimationLeftStart = Left;
        AnimationLeftEnd = Left;
        AnimationDuration = TimeSpan.FromMilliseconds(150);
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
        Topmost = true;
        double scaleFactor = .3;
        AnimationStartHeight = ActualHeight * scaleFactor;
        AnimationEndHeight = ActualHeight;
        AnimationTopStart = Top + AnimationStartHeight / 2 / 2;
        AnimationTopEnd = Top;

        AnimationStartWidth = ActualWidth * scaleFactor;
        AnimationEndWidth = ActualWidth;
        AnimationLeftStart = Left;
        AnimationLeftEnd = Left;

        //var hwndSource = (HwndSource)PresentationSource.FromVisual(target);
        //CloakHelper.Cloak(hwndSource.Handle);
        AnimationDuration = TimeSpan.FromMilliseconds(150);
        OpacityStart = 0.0;
        OpacityEnd = 1;
        OnAnimationEnd = () =>
        {
            Close();
            target.Show();
            //target.Left = xMid;
        };

        ;
        if (Resources["FadeOut"] is Storyboard sb)
        {
            sb.Begin();
        }
    }

    private static double Midpoint(double start, double stop)
    {
        return start + ((stop - start) / 2);
    }

    private void FadeOutStoryboard_Completed(object sender, EventArgs e)
    {
        OnAnimationEnd?.Invoke();
    }
}
