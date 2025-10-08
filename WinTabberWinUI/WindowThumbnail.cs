using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace WinTabberUI
{
    public class WindowThumbnail : FrameworkElement
    {
        public WindowThumbnail()
        {
            if (!IsDwmEnabled)
                throw new NotSupportedException("Creating a window thumbnail is not supported when DWM is not enabled.");

            LayoutUpdated += new EventHandler(Thumbnail_LayoutUpdated);
            Unloaded += new RoutedEventHandler(Thumbnail_Unloaded);
        }

        private static bool IsDwmEnabled
        {
            get
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    PInvoke.DwmIsCompositionEnabled(out var enabled);
                    return enabled;
                }

                return false;
            }
        }

        public static DependencyProperty SourceProperty;
        public static DependencyProperty ClientAreaOnlyProperty;

        static WindowThumbnail()
        {
            SourceProperty = DependencyProperty.Register(
                "Source",
                typeof(IntPtr),
                typeof(WindowThumbnail),
                new FrameworkPropertyMetadata(
                    IntPtr.Zero,
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    delegate (DependencyObject obj, DependencyPropertyChangedEventArgs args)
                    {
                        ((WindowThumbnail)obj).InitialiseThumbnail((IntPtr)args.NewValue);
                    }));

            ClientAreaOnlyProperty = DependencyProperty.Register(
                "ClientAreaOnly",
                typeof(bool),
                typeof(WindowThumbnail),
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    delegate (DependencyObject obj, DependencyPropertyChangedEventArgs args)
                    {
                        ((WindowThumbnail)obj).UpdateThumbnail();
                    }));

            OpacityProperty.OverrideMetadata(
                typeof(WindowThumbnail),
                new FrameworkPropertyMetadata(
                    1.0,
                    FrameworkPropertyMetadataOptions.Inherits,
                    delegate (DependencyObject obj, DependencyPropertyChangedEventArgs args)
                    {
                        ((WindowThumbnail)obj).UpdateThumbnail();
                    }));
        }

        public IntPtr Source
        {
            get { return (IntPtr)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        public bool ClientAreaOnly
        {
            get { return (bool)GetValue(ClientAreaOnlyProperty); }
            set { SetValue(ClientAreaOnlyProperty, value); }
        }

        public new double Opacity
        {
            get { return (double)GetValue(OpacityProperty); }
            set { SetValue(OpacityProperty, value); }
        }

        private HwndSource? _target;
        private IntPtr _thumb = IntPtr.Zero;

        private void InitialiseThumbnail(IntPtr source)
        {
            if (IntPtr.Zero != _thumb)
            {
                // release the old thumbnail
                ReleaseThumbnail();
            }

            if (IntPtr.Zero != source)
            {
                // find our parent hwnd
                _target = (HwndSource)HwndSource.FromVisual(this);

                // if we have one, we can attempt to register the thumbnail
                if (_target != null && 0 == PInvoke.DwmRegisterThumbnail(new HWND(_target.Handle), new HWND(source), out _thumb))
                {
                    var props = new DWM_THUMBNAIL_PROPERTIES();
                    props.fVisible = false;
                    props.fSourceClientAreaOnly= ClientAreaOnly;
                    props.opacity = (byte)(255 * Opacity);
                    props.dwFlags = PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_SOURCECLIENTAREAONLY
                        | PInvoke.DWM_TNP_OPACITY;
                    PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
                }
            }
        }

        private void ReleaseThumbnail()
        {
            PInvoke.DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
            _target = null;
        }

        private void UpdateThumbnail()
        {
            if (IntPtr.Zero != _thumb)
            {                
                var props = new DWM_THUMBNAIL_PROPERTIES();
                props.fSourceClientAreaOnly = ClientAreaOnly;
                props.opacity = (byte)(255 * Opacity);
                props.dwFlags = PInvoke.DWM_TNP_SOURCECLIENTAREAONLY | PInvoke.DWM_TNP_OPACITY;
                PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
            }
        }

        private void Thumbnail_Unloaded(object sender, RoutedEventArgs e)
        {
            ReleaseThumbnail();
        }

        // this is where the magic happens
        private void Thumbnail_LayoutUpdated(object? sender, EventArgs e)
        {
            if (IntPtr.Zero == _thumb)
            {
                InitialiseThumbnail(Source);
            }

            if (IntPtr.Zero != _thumb)
            {
                if (_target.RootVisual == null || !_target.RootVisual.IsAncestorOf(this))
                {
                    //we are no longer in the visual tree
                    ReleaseThumbnail();
                    return;
                }

                GeneralTransform transform = TransformToAncestor(_target.RootVisual);

                Point a = transform.Transform(new Point(0, 0));
                if (double.IsNaN(a.X))
                {
                    InvalidateArrange();
                }
                else
                {
                    Point b = transform.Transform(new Point(ActualWidth, ActualHeight));
                    var dpiInfo = VisualTreeHelper.GetDpi(this);

                    var props = new DWM_THUMBNAIL_PROPERTIES();
                    props.fVisible = true;
                    props.rcDestination = new RECT
                    {
                        left = (int)Math.Ceiling(a.X * dpiInfo.DpiScaleX),
                        top = (int)Math.Ceiling(a.Y * dpiInfo.DpiScaleY),
                        right = (int)Math.Ceiling(b.X * dpiInfo.DpiScaleX),
                        bottom = (int)Math.Ceiling(b.Y * dpiInfo.DpiScaleY)
                    };
                        
                    props.dwFlags = PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_RECTDESTINATION;
                    PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
                }
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);

            double scale = 1;

            // our preferred size is the thumbnail source size
            // if less space is available, we scale appropriately
            if (size.Width > availableSize.Width)
                scale = availableSize.Width / size.Width;
            if (size.Height > availableSize.Height)
                scale = Math.Min(scale, availableSize.Height / size.Height);

            return new Size(size.Width * scale, size.Height * scale); ;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);

            // scale to fit whatever size we were allocated
            double scale = finalSize.Width / size.Width;
            scale = Math.Min(scale, finalSize.Height / size.Height);

            return new Size(size.Width * scale, size.Height * scale);
        }
    }
}
