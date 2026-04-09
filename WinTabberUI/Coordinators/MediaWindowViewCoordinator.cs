using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class MediaWindowViewCoordinator : ViewCoordinatorBase<MediaControlsWindow>
    {
        private ApplicationStateViewModel _vm;

        public MediaWindowViewCoordinator(ApplicationStateViewModel vm, IServiceProvider provider)
            : base(provider)
        {
            ReuseInstances = true;
            _vm = vm;
        }
        protected override void Close(MediaControlsWindow instance)
        {
            //WindowRasterizationHelper.AnimateHide(instance);
            //instance.Hide();
            AnimateClose(instance);
            

        }

        private void AnimateClose(MediaControlsWindow instance)
        {
            //instance.Hide();
            //return;
            double time = 150;

            var anim = new ByteAnimation(255, 0, new Duration(TimeSpan.FromMilliseconds(time)))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }

            };
            //instance.SizeToContent = SizeToContent.WidthAndHeight;

            //var anim2 = new DoubleAnimation(instance.ActualWidth, .8 * instance.ActualWidth, new Duration(TimeSpan.FromMilliseconds(time)))
            //{
            //    FillBehavior = FillBehavior.Stop,
            //    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            //};


            //var anim3 = new DoubleAnimation(instance.ActualHeight, 1 * instance.ActualHeight, new Duration(TimeSpan.FromMilliseconds(time)))
            //{
            //    FillBehavior = FillBehavior.Stop,
            //    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }

            //};


            Storyboard.SetTarget(anim, instance);
            Storyboard.SetTargetProperty(anim, new PropertyPath(WindowHelper2.WindowAlphaProperty));



            var sb = new Storyboard
            {
                Children = {
                    anim,
                    //anim2,
                    //anim3
                },
            };

            EventHandler? handler = null;

            handler = (s, e) =>
            {
                //instance.Opacity = 1;
                if (VisualTreeHelper.GetChild(instance, 0) is FrameworkElement elem)
                {
                    //elem.RenderTransform = null;

                }
                WindowHelper2.ApplyOpacity(instance, 0);
                instance.Hide();
                //instance.SizeToContent = SizeToContent.WidthAndHeight;

                sb.Completed -= handler;
            };

            sb.Completed += handler;



            //var scale = new ScaleTransform(0, 0, instance.Width / 2, instance.Height / 2);
            //    //Storyboard.SetTarget(anim3, instance);
            ////instance.BeginStoryboard(sb);
            ////instance.RenderTransform = scale;
            //if (VisualTreeHelper.GetChild(instance, 0) is FrameworkElement elem)
            //{
            //    //Storyboard.SetTarget(anim2, instance);
            //    instance.SizeToContent = SizeToContent.WidthAndHeight;
            //    //Storyboard.SetTargetProperty(anim2, new PropertyPath(Window.WidthProperty));
            //    //Storyboard.SetTargetProperty(anim3, new PropertyPath(Window.HeightProperty));
            //    //Storyboard.SetTargetProperty(anim2, new PropertyPath("(FrameworkElement.RenderTransform).(ScaleTransform.ScaleY)"));
            //    //Storyboard.SetTargetProperty(anim3, new PropertyPath("(FrameworkElement.RenderTransform).(ScaleTransform.ScaleX)"));
            //    //elem.RenderTransform = scale;


            //}
            //instance.SizeToContent = SizeToContent.Manual;

            sb.Begin();
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsMediaControlsActiveChanges;
        }


        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private void DisableRedraw(System.Windows.Media.Visual control)
        {
            var hwndSource = PresentationSource.FromVisual(control) as HwndSource;
            if (hwndSource != null)
            {
                SendMessage(hwndSource.Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);
            }
        }

        private void EnableRedraw(System.Windows.Media.Visual control)
        {
            var hwndSource = PresentationSource.FromVisual(control) as HwndSource;
            if (hwndSource != null)
            {
                SendMessage(hwndSource.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                // Invalidate the control to force a repaint after re-enabling redraw
                // This is often necessary in Win32 to ensure the window repaints
                if(hwndSource.RootVisual is UIElement e)
                {
                    e.InvalidateVisual();
                }
            }
        }
        protected override void Show(MediaControlsWindow instance)
        {
            //WindowRasterizationHelper.AnimateShow(instance);
            //instance.Opacity = 0;
            //instance.SizeToContent = SizeToContent.WidthAndHeight;
            
            //var hwnd = new WindowInteropHelper(instance).Handle;
            //DisableRedraw(instance);
            //if(hwnd > 0)
            //{
                //CloakHelper.Cloak(hwnd);
            //}
            var top = instance.Top;
            var left = instance.Left;

            instance.Top = -99999;
            instance.Left = -99999;
            instance.Show();
            //DisableRedraw(instance);

            //hwnd = new WindowInteropHelper(instance).Handle;
            //CloakHelper.Cloak(hwnd);

            //WindowHelper.SetSystemBackdropType(instance, iNKORE.UI.WPF.Modern.Helpers.Styles.BackdropType.Acrylic11);
            //WindowHelper.SetAcrylic10Color(instance, Colors.Red);
            //WindowHelper2.SetWindowAlpha(instance, 80);
            //WindowHelper2.SetWindowAlpha(instance, 100);
            //WindowHelper2.Reset(instance);
            WindowHelper2.ApplyOpacity(instance, 0);
            //CloakHelper.Uncloak(hwnd);
            instance.Top = top;
            instance.Left = left;
            double time = 500;

            //var anim = new DoubleAnimation(.01, 1,
            //    new Duration(TimeSpan.FromMilliseconds(time)))
            //{
            //    FillBehavior = FillBehavior.Stop,
            //    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }

            //};

            var anim = new ByteAnimation(0, 255, new Duration(TimeSpan.FromMilliseconds(time)))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction =  new SineEase() //new SineEase { EasingMode = EasingMode.EaseInOut }

            };

            //var anim2 = new DoubleAnimation(0.8 * instance.ActualWidth, instance.ActualWidth, new Duration(TimeSpan.FromMilliseconds(time)))
            //{
            //    FillBehavior = FillBehavior.Stop,
            //    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }

            //};

            //var anim3 = new DoubleAnimation(0 * instance.ActualHeight, 1 *instance.ActualHeight, new Duration(TimeSpan.FromMilliseconds(time)))
            //{
            //    FillBehavior = FillBehavior.Stop,
            //    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }

            //};


            Storyboard.SetTarget(anim, instance);
            //Storyboard.SetTargetProperty(anim, new PropertyPath(Window.OpacityProperty));
            Storyboard.SetTargetProperty(anim, new PropertyPath(WindowHelper2.WindowAlphaProperty));



            var sb = new Storyboard
            {
                Children = {
                    anim,
                    //anim2,
                    //anim3
                },
            };

            EventHandler? handler = null;

            handler = (s, e) =>
            {
                //instance.Opacity = 1;
                
                //instance.SizeToContent = SizeToContent.WidthAndHeight;

                EnableRedraw(instance);
                sb.Completed -= handler;
            };

            sb.Completed += handler;


            //var scale = new ScaleTransform(1, 1, instance.Width / 2, instance.Height / 2);
            //////instance.BeginStoryboard(sb);
            //////instance.RenderTransform = scale;
            //if (VisualTreeHelper.GetChild(instance, 0) is FrameworkElement elem)
            //{
            //    Storyboard.SetTarget(anim2, instance);
            //    Storyboard.SetTarget(anim3, instance);
            //    //Storyboard.SetTargetProperty(anim2, new PropertyPath(Window.WidthProperty));
            //    //Storyboard.SetTargetProperty(anim3, new PropertyPath(Window.HeightProperty));
            //    //elem.RenderTransform = scale;

            //}
            ////instance.SizeToContent = SizeToContent.Manual;
            sb.Begin();
        }
    }
}
