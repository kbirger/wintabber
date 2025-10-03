using System;
using System.Collections.Generic;
using System.Diagnostics;
//using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.Win32;
using WinTabber.API;
using WinTabber.Interop;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrackBar;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for ThumbnailContainer.xaml
    /// </summary>
    public partial class ThumbnailContainer : UserControl
    {
        private nint? id = null;
        private nint handle = 0;
        public ThumbnailContainer()
        {
            InitializeComponent();
            Loaded += ThumbnailContainer_Loaded;
            DataContextChanged += ThumbnailContainer_DataContextChanged;
            MouseUp += ThumbnailContainer_MouseUp;
            LayoutUpdated += ThumbnailContainer_LayoutUpdated;
            Unloaded += ThumbnailContainer_Unloaded;
        }

        private void ThumbnailContainer_Unloaded(object sender, RoutedEventArgs e)
        {
            if (id != null)
            {
                PInvoke.DwmUnregisterThumbnail(id.Value);
                id = null;
                handle = 0;
            }
        }

        private void ThumbnailContainer_LayoutUpdated(object? sender, EventArgs e)
        {
            InitializeThumb();
            RenderThumb();
        }

        private void ThumbnailContainer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            RenderThumb();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            //if(id is not null)
            //{
            //    PInvoke.DwmQueryThumbnailSourceSize(id.Value, out var sourceSize);
            //    return new Size(sourceSize.Width, sourceSize.Height);
            //}
            return base.MeasureOverride(constraint);
        }

        protected override void ParentLayoutInvalidated(UIElement child)
        {
            RenderThumb();
            //base.ParentLayoutInvalidated(child);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            RenderThumb();
            base.OnRenderSizeChanged(sizeInfo);
        }

        private void RenderThumb()
        {
            //if (id != null)
            //{
            //    PInvoke.DwmUnregisterThumbnail(id.Value);
            //    id = null;
            //}

            if (DataContext is WindowItem wi && wi.WindowRef.Handle != 0)
            {
                var thisWindowHandle = new WindowInteropHelper(Application.Current.MainWindow);
                //var x = new WindowRef(Handle, new WindowProcessRef(Process.GetCurrentProcess(), new ApplicationRef("foo", new WindowManager(new InteropProxy()))));
                nint? source = (nint?)(DataContext as WindowItem)?.WindowRef.Handle;
                if (source == null || id == null) return;


                //var target = HwndSource.FromVisual(TargetElement) as HwndSource;

                //if(target == null) return;
                //var result = PInvoke.DwmRegisterThumbnail(
                //    new Windows.Win32.Foundation.HWND(target.Handle),
                //    new Windows.Win32.Foundation.HWND(source),
                //    out var thumbId);
                //id = thumbId;
                var target = HwndSource.FromVisual(this) as HwndSource;
                if(target == null) return;

                var rxx = PInvoke.DwmQueryThumbnailSourceSize(id.Value, out var sourceSize);
                var transform = TransformToAncestor(target.RootVisual);
                Width = sourceSize.Width;
                Height = sourceSize.Height;
                Point a = transform.Transform(new Point(0, 0));

                Point b = transform.Transform(new Point(ActualWidth, ActualHeight));
                //var availableBounds = BoundsRelativeTo(this, Application.Current.MainWindow);
                //PresentationSource source = PresentationSource.FromVisual(this);


                //double dpiX = 96, dpiY = 96;
                //if (source != null)
                //{
                //    dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                //    dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
                //}

                //var scaleX = dpiX / 96.0;
                //var scaleY = dpiY / 96.0;
                //scaleX = 1.53;
                //availableBounds.X *= scaleX;
                //availableBounds.Y *= scaleY;
                //availableBounds.Width *= scaleX;
                //availableBounds.Height *= scaleY;

                //var fittedSize = FitRectangle(new Size(availableBounds.Width * scaleX, availableBounds.Height * scaleY), new Size(sourceSize.Width * scaleX, sourceSize.Height * scaleY));
                //var (w, h) = GetWindowFit(sourceSize.Width, sourceSize.Height, bounds.Height, bounds.Height);
                var thumbProps = new Windows.Win32.Graphics.Dwm.DWM_THUMBNAIL_PROPERTIES();
                thumbProps.dwFlags = PInvoke.DWM_TNP_RECTDESTINATION | PInvoke.DWM_TNP_VISIBLE;
                thumbProps.fVisible = true;
                var dpiInfo = VisualTreeHelper.GetDpi(this);
                thumbProps.rcDestination = new Windows.Win32.Foundation.RECT
                {
                    left = (int)Math.Ceiling(a.X * dpiInfo.DpiScaleX),
                    top = (int)Math.Ceiling(a.Y * dpiInfo.DpiScaleY),
                    right = (int)Math.Ceiling(b.X * dpiInfo.DpiScaleX),
                    bottom = (int)Math.Ceiling(b.Y * dpiInfo.DpiScaleY)
                    //left = (int)(availableBounds.Left * scaleX),
                    //top = (int)(availableBounds.Top * scaleY),
                    //right = (int)((availableBounds.Left * scaleX + availableBounds.Width * scaleX)),
                    //bottom = (int)((availableBounds.Top * scaleY + fittedSize.Height))
                };
                var res2 = PInvoke.DwmUpdateThumbnailProperties(id.Value, thumbProps);
            }
        }
        private void ThumbnailContainer_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            InitializeThumb();
            //RenderThumb();
        }

        private void InitializeThumb()
        {
            nint? source = (nint?)(DataContext as WindowItem)?.WindowRef.Handle;
            if (source == handle)
            {
                return;
            }
            if (id != null)
            {
                PInvoke.DwmUnregisterThumbnail(id.Value);
                id = null;
                handle = 0;
            }

            var target = HwndSource.FromVisual(this) as HwndSource;

            if (target == null || source == null) return;
            var result = PInvoke.DwmRegisterThumbnail(
                new Windows.Win32.Foundation.HWND(target.Handle),
                new Windows.Win32.Foundation.HWND(source.Value),
                out var thumbId);
            id = thumbId;
            handle = source.Value;

            var thumbProps = new Windows.Win32.Graphics.Dwm.DWM_THUMBNAIL_PROPERTIES();
            thumbProps.dwFlags = PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_OPACITY;
            var dpiInfo = VisualTreeHelper.GetDpi(this);

            thumbProps.fVisible = true;
            thumbProps.opacity = 255; // Fully opaque
            var res2 = PInvoke.DwmUpdateThumbnailProperties(thumbId, thumbProps);
        }


        private void ThumbnailContainer_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeThumb();
            //var foo = new Foo();

            //var helper = new WindowInteropHelper(Application.Current.MainWindow);
            ////var x = new WindowRef(Handle, new WindowProcessRef(Process.GetCurrentProcess(), new ApplicationRef("foo", new WindowManager(new InteropProxy()))));
            //var hnd = (DataContext as WindowItem)?.WindowRef.Handle;
            //if (hnd == null) return;
            //var result = PInvoke.DwmRegisterThumbnail(
            //    new Windows.Win32.Foundation.HWND(helper.Handle),
            //    new Windows.Win32.Foundation.HWND((nint)hnd),
            //    out var id);


            //var thumbProps = new Windows.Win32.Graphics.Dwm.DWM_THUMBNAIL_PROPERTIES();
            //thumbProps.dwFlags = PInvoke.DWM_TNP_RECTDESTINATION | PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_OPACITY;
            //thumbProps.rcDestination = new Windows.Win32.Foundation.RECT { left = 0, top = 0, right = 200, bottom = 250 }; // Example
            //thumbProps.fVisible = true;
            //thumbProps.opacity = 255; // Fully opaque
            //var res2 = PInvoke.DwmUpdateThumbnailProperties(id, thumbProps);
        }

        //private DependencyProperty _handle = DependencyProperty.Register(
        //    "Handle",
        //    typeof(int),
        //    typeof(ThumbnailContainer),
        //    new PropertyMetadata());

        //public int Handle
        //{
        //    get => (int)GetValue(_handle);
        //    set => SetValue(_handle, value);
        //}
        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            base.OnMouseDoubleClick(e);

        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            RenderThumb();
            base.OnRender(drawingContext);


        }
    }
}
