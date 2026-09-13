using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using WinRT.Interop;

namespace WinTabberUI.Controls;

public class WindowThumbnail : FrameworkElement
{
    public WindowThumbnail()
    {
        if (!IsDwmEnabled)
            throw new NotSupportedException("Creating a window thumbnail is not supported when DWM is not enabled.");

        LayoutUpdated += Thumbnail_LayoutUpdated;
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            // LayoutUpdated commonly fires before Loaded (unlike WPF's IsAncestorOf check, which is
            // already true once the element is in the tree), and releases the thumbnail it just
            // registered because _isLoaded was still false. Nothing else invalidates layout
            // afterward, so without this the thumbnail would stay released forever. One explicit
            // InvalidateArrange here schedules the follow-up LayoutUpdated pass that (re)registers it
            // now that _isLoaded is true.
            InvalidateArrange();
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            ReleaseThumbnail();
        };
    }

    private static bool IsDwmEnabled
    {
        get
        {
            PInvoke.DwmIsCompositionEnabled(out var enabled);
            return enabled;
        }
    }

    // Registered as `long`, not `nint`/`IntPtr`: WinRT has no ABI representation for IntPtr, so
    // boxing an IntPtr default value for PropertyMetadata throws (CsWinRT can't produce an
    // IReference<IntPtr>). The public Source property below still exposes `nint` as the brief
    // specifies; only the DependencyProperty's storage type differs.
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(long), typeof(WindowThumbnail),
        new PropertyMetadata((long)0, (d, e) =>
        {
            var self = (WindowThumbnail)d;
            self.InitialiseThumbnail((nint)(long)e.NewValue);
            // InitialiseThumbnail registers with fVisible = false; only a LayoutUpdated pass sets it
            // visible and computes rcDestination. If Source changes after Loaded with no other layout
            // activity pending, nothing would schedule that pass and the thumbnail would sit registered
            // but invisible forever. Force one, same as the Loaded handler does.
            self.InvalidateArrange();
        }));

    public static readonly DependencyProperty ClientAreaOnlyProperty = DependencyProperty.Register(
        nameof(ClientAreaOnly), typeof(bool), typeof(WindowThumbnail),
        new PropertyMetadata(false, (d, _) => ((WindowThumbnail)d).UpdateThumbnail()));

    // When true, always fills exactly the space it's given (DWM stretches the bitmap to match,
    // non-uniformly if the aspect ratio doesn't line up) instead of computing an aspect-preserving,
    // letterboxed size. Off by default so existing consumers (e.g. the selector tiles) keep their
    // current letterboxed behavior.
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(bool), typeof(WindowThumbnail), new PropertyMetadata(false));

    // Replaces WPF's HwndSource.FromVisual(this) auto-discovery, which has no WinUI 3 equivalent —
    // a FrameworkElement cannot discover its owning Window from the visual tree alone. The hosting
    // window (DockWindow, ThumbnailWindow) sets this once after it obtains its own HWND.
    public static readonly DependencyProperty TargetWindowProperty = DependencyProperty.Register(
        nameof(TargetWindow), typeof(Window), typeof(WindowThumbnail),
        new PropertyMetadata(null, (d, e) =>
        {
            var self = (WindowThumbnail)d;
            self.InitialiseThumbnail(self.Source);
            // Same reasoning as the Source-changed callback above: without this, a TargetWindow change
            // after Loaded (e.g. DockWindow re-targeting its thumbnail) can leave the thumbnail
            // registered but permanently invisible.
            self.InvalidateArrange();
        }));

    public nint Source
    {
        get => (nint)(long)GetValue(SourceProperty);
        set => SetValue(SourceProperty, (long)value);
    }

    public bool ClientAreaOnly
    {
        get => (bool)GetValue(ClientAreaOnlyProperty);
        set => SetValue(ClientAreaOnlyProperty, value);
    }

    public bool Stretch
    {
        get => (bool)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public Window? TargetWindow
    {
        get => (Window?)GetValue(TargetWindowProperty);
        set => SetValue(TargetWindowProperty, value);
    }

    private nint _targetHwnd;
    private nint _thumb;
    private bool _isLoaded;

    private void InitialiseThumbnail(nint source)
    {
        if (_thumb != 0)
        {
            ReleaseThumbnail();
        }

        if (source != 0 && TargetWindow is { } window)
        {
            _targetHwnd = WindowNative.GetWindowHandle(window);

            if (_targetHwnd != 0 && 0 == PInvoke.DwmRegisterThumbnail(new HWND(_targetHwnd), new HWND(source), out var thumb))
            {
                _thumb = thumb;
                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    fVisible = false,
                    fSourceClientAreaOnly = ClientAreaOnly,
                    opacity = 255,
                    dwFlags = PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_SOURCECLIENTAREAONLY | PInvoke.DWM_TNP_OPACITY,
                };
                PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
            }
        }
    }

    private void ReleaseThumbnail()
    {
        if (_thumb != 0)
        {
            PInvoke.DwmUnregisterThumbnail(_thumb);
        }
        _thumb = 0;
        _targetHwnd = 0;
    }

    private void UpdateThumbnail()
    {
        if (_thumb != 0)
        {
            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                fSourceClientAreaOnly = ClientAreaOnly,
                opacity = 255,
                dwFlags = PInvoke.DWM_TNP_SOURCECLIENTAREAONLY | PInvoke.DWM_TNP_OPACITY,
            };
            PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
        }
    }

    // this is where the magic happens
    private void Thumbnail_LayoutUpdated(object? sender, object e)
    {
        if (_thumb == 0)
        {
            InitialiseThumbnail(Source);
        }

        if (_thumb != 0)
        {
            if (!_isLoaded || TargetWindow is not { } window)
            {
                ReleaseThumbnail();
                return;
            }

            var root = window.Content;
            if (root is null)
            {
                InvalidateArrange();
                return;
            }

            var transform = TransformToVisual(root);
            var a = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            if (double.IsNaN(a.X))
            {
                InvalidateArrange();
            }
            else
            {
                var b = transform.TransformPoint(new Windows.Foundation.Point(ActualSize.X, ActualSize.Y));
                var scale = XamlRoot?.RasterizationScale ?? 1.0;

                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    fVisible = true,
                    rcDestination = new RECT
                    {
                        left = (int)Math.Ceiling(a.X * scale),
                        top = (int)Math.Ceiling(a.Y * scale),
                        right = (int)Math.Ceiling(b.X * scale),
                        bottom = (int)Math.Ceiling(b.Y * scale),
                    },
                    dwFlags = PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_RECTDESTINATION,
                };
                PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Stretch)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        if (_thumb == 0)
        {
            return new Size(0, 0);
        }

        PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);
        double scale = 1;
        if (size.Width > availableSize.Width) scale = availableSize.Width / size.Width;
        if (size.Height > availableSize.Height) scale = Math.Min(scale, availableSize.Height / size.Height);
        return new Size(size.Width * scale, size.Height * scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Stretch || _thumb == 0)
        {
            return finalSize;
        }

        PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);
        double scale = finalSize.Width / size.Width;
        scale = Math.Min(scale, finalSize.Height / size.Height);
        return new Size(size.Width * scale, size.Height * scale);
    }
}
