using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.UI.Composition;
using Windows.UI.Core.AnimationMetrics;
using WinTabberUI.Helpers;
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
            WindowRasterizationHelper.AnimateHide(instance);
            //instance.Hide();
            

        }

        private void AnimateClose(MediaControlsWindow instance)
        {
            double time = 150;
            var anim = new DoubleAnimation(1, 0,
                new Duration(TimeSpan.FromMilliseconds(time)))
            {
                FillBehavior = FillBehavior.Stop
            };
            //instance.SizeToContent = SizeToContent.WidthAndHeight;

            var anim2 = new DoubleAnimation(instance.ActualWidth, .8 * instance.ActualWidth, new Duration(TimeSpan.FromMilliseconds(time)))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };


            var anim3 = new DoubleAnimation(instance.ActualHeight, 1 * instance.ActualHeight, new Duration(TimeSpan.FromMilliseconds(time)))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }

            };


            Storyboard.SetTarget(anim, instance);
            Storyboard.SetTargetProperty(anim, new PropertyPath(Window.OpacityProperty));



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
                instance.Opacity = 1;
                if (VisualTreeHelper.GetChild(instance, 0) is FrameworkElement elem)
                {
                    elem.RenderTransform = null;

                }
                instance.Hide();
                //instance.SizeToContent = SizeToContent.WidthAndHeight;

                sb.Completed -= handler;
            };

            sb.Completed += handler;



            var scale = new ScaleTransform(0, 0, instance.Width / 2, instance.Height / 2);
            //instance.BeginStoryboard(sb);
            //instance.RenderTransform = scale;
            if (VisualTreeHelper.GetChild(instance, 0) is FrameworkElement elem)
            {
                Storyboard.SetTarget(anim2, instance);
                Storyboard.SetTarget(anim3, instance);
                instance.SizeToContent = SizeToContent.WidthAndHeight;
                Storyboard.SetTargetProperty(anim2, new PropertyPath(Window.WidthProperty));
                Storyboard.SetTargetProperty(anim3, new PropertyPath(Window.HeightProperty));
                //Storyboard.SetTargetProperty(anim2, new PropertyPath("(FrameworkElement.RenderTransform).(ScaleTransform.ScaleY)"));
                //Storyboard.SetTargetProperty(anim3, new PropertyPath("(FrameworkElement.RenderTransform).(ScaleTransform.ScaleX)"));
                //elem.RenderTransform = scale;


            }
            //instance.SizeToContent = SizeToContent.Manual;

            sb.Begin();
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsMediaControlsActiveChanges;
        }

        protected override void Show(MediaControlsWindow instance)
        {
            WindowRasterizationHelper.AnimateShow(instance);
            //instance.Opacity = 0;
            //instance.SizeToContent = SizeToContent.WidthAndHeight;
            //instance.Show();
            //double time = 100;

            //var anim = new DoubleAnimation(.01, 1,
            //    new Duration(TimeSpan.FromMilliseconds(time)))
            //{
            //    FillBehavior = FillBehavior.Stop,
            //    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }

            //};

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


            //Storyboard.SetTarget(anim, instance);
            //Storyboard.SetTargetProperty(anim, new PropertyPath(Window.OpacityProperty));



            //var sb = new Storyboard
            //{
            //    Children = {
            //        anim,
            //        //anim2,
            //        //anim3
            //    },
            //};

            //EventHandler? handler = null;

            //handler = (s, e) =>
            //{
            //    instance.Opacity = 1;
            //    //instance.SizeToContent = SizeToContent.WidthAndHeight;


            //    sb.Completed -= handler;
            //};

            //sb.Completed += handler;


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
            //sb.Begin();
        }
    }
}
