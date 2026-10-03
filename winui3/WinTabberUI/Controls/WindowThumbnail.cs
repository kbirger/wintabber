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
            // InvalidateMeasure here schedules the follow-up LayoutUpdated pass that (re)registers it
            // now that _isLoaded is true.
            //
            // This is the actual fix for the Task 4a.4 review bug: the original code called only
            // InvalidateArrange() here. On the very first layout pass, MeasureOverride ran before
            // DwmRegisterThumbnail (_thumb == 0) and committed a (0,0) DesiredSize; InvalidateArrange
            // alone only re-runs Arrange against that already-committed size and can never enlarge
            // it, so the control stayed permanently zero-sized for any consumer that doesn't pin an
            // explicit Width/Height. InvalidateMeasure forces a fresh Measure pass, which now sees a
            // non-zero _thumb and can commit a real DesiredSize.
            //
            // We call both, rather than relying on InvalidateMeasure alone: WPF's
            // UIElement.InvalidateMeasure docs state it "also calls InvalidateArrange internally", so
            // one call suffices there. The WinUI 3 Microsoft.UI.Xaml.UIElement doc page for the same
            // method omits that sentence (it only says UpdateLayout is "equivalent to calling
            // InvalidateMeasure and InvalidateArrange in sequence") — which isn't proof WinUI 3
            // behaves differently, but isn't proof it doesn't either, and we did not find an
            // authoritative source settling it either way. Calling both costs nothing and removes the
            // question, so we don't depend on undocumented chaining behavior.
            InvalidateMeasure();
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

            // REAL BUG found via live verification (WindowSelectorWindow, this task's own
            // follow-up): registering eagerly here before Loaded has fired raced against
            // Thumbnail_LayoutUpdated's `!_isLoaded` release guard -- Source/TargetWindow are
            // routinely both set (via x:Bind/code) before the control's Loaded event has actually
            // fired, so InitialiseThumbnail would succeed here, then the very next LayoutUpdated
            // pass (scheduled by the InvalidateMeasure/Arrange below) would see _isLoaded still
            // false and immediately release what was just registered -- confirmed live via a
            // call-count log showing DwmRegisterThumbnail immediately followed by
            // "releasing: isLoaded=False" on the same instance, repeatedly. Skipping registration
            // here until Loaded has actually fired removes that race: the Loaded handler's own
            // InvalidateMeasure already schedules a LayoutUpdated pass whose lazy `_thumb == 0`
            // branch registers using whatever Source/TargetWindow are set by then, so no update is
            // lost, only deferred to a point where the isLoaded guard cannot immediately undo it.
            if (!self._isLoaded)
            {
                return;
            }

            self.InitialiseThumbnail((nint)(long)e.NewValue);
            // InitialiseThumbnail registers with fVisible = false; only a LayoutUpdated pass sets it
            // visible and computes rcDestination. If Source changes after Loaded with no other layout
            // activity pending, nothing would schedule that pass and the thumbnail would sit registered
            // but invisible forever. Force one, same as the Loaded handler does — both
            // InvalidateMeasure (so a zero-area DesiredSize committed before this Source was set can
            // grow now that _thumb is non-zero) and InvalidateArrange (see the Loaded handler's
            // comment for why we call both rather than relying on one implying the other).
            self.InvalidateMeasure();
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

            // See the Source-changed callback's doc comment for why registration is skipped here
            // until Loaded has actually fired -- the same register-then-immediately-release race
            // applies to this callback too (and was the more commonly hit one live, since
            // TargetWindow is set from code shortly after the container is realized, routinely
            // before Loaded fires).
            if (!self._isLoaded)
            {
                return;
            }

            self.InitialiseThumbnail(self.Source);
            // Same reasoning as the Source-changed callback above: without this, a TargetWindow change
            // after Loaded (e.g. DockWindow re-targeting its thumbnail) can leave the thumbnail
            // registered but permanently invisible, and DesiredSize stuck at zero. Both
            // InvalidateMeasure and InvalidateArrange are called — see the Loaded handler's comment.
            self.InvalidateMeasure();
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
            var hr = PInvoke.DwmRegisterThumbnail(new HWND(_targetHwnd), new HWND(source), out var thumb);

            if (_targetHwnd != 0 && 0 == hr)
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
        // REAL BUG found via live verification (this task's own second follow-up): the register-
        // then-immediately-release race documented on the Source/TargetWindow callbacks above also
        // applies here, and this lazy branch is the DOMINANT registration path in practice (it runs
        // on every LayoutUpdated regardless of what triggered it, unlike the property-changed
        // callbacks which only run once per actual value change) -- guarding only the callbacks left
        // this branch registering while _isLoaded was still false just as often, confirmed live via
        // the same register/release churn persisting after that first guard was added. Skipping here
        // too means no registration attempt happens at all until Loaded has fired; the Loaded
        // handler's own InvalidateMeasure schedules the next LayoutUpdated pass that retries once
        // _isLoaded is genuinely true.
        if (_thumb == 0 && _isLoaded)
        {
            InitialiseThumbnail(Source);

            if (_thumb != 0)
            {
                // Registered for the first time via this lazy path. A temporary diagnostic run
                // against the real DockWindow (not a synthetically-sized harness) showed this is
                // where registration actually succeeds for the containers that persist on screen —
                // the three callbacks above (Loaded, Source-changed, TargetWindow-changed) had
                // already run and invalidated measure/arrange by this point, but _thumb was still 0
                // when they did, so their InitialiseThumbnail calls were no-ops.
                //
                // By this point in the CURRENT pass, MeasureOverride already ran with _thumb == 0
                // and committed a (0,0) DesiredSize, and Arrange already ran against that, so
                // ActualSize below is still stale (0,0) — using it now would compute the same
                // degenerate zero-area rcDestination this whole fix exists to avoid. Force a fresh
                // Measure/Arrange pass and pick up a real rcDestination on the next LayoutUpdated
                // instead of computing one now from stale state.
                //
                // This early return skips the _isLoaded/TargetWindow-null release guard just below
                // for this one pass. If _isLoaded happens to be false here (LayoutUpdated can fire
                // before Loaded — see that handler's comment), the thumbnail stays registered but
                // unreleased for one extra pass instead of being released immediately; the guard
                // still runs and releases it on the very next pass. Verified via the same diagnostic
                // run to converge correctly in practice, not left as a bare assumption.
                InvalidateMeasure();
                InvalidateArrange();
                return;
            }
        }

        if (_thumb != 0)
        {
            if (!_isLoaded || TargetWindow is not { } window)
            {
                ReleaseThumbnail();
                return;
            }

            // REAL BUG found via live verification: un-thumbnailing (closing ThumbnailWindow) threw
            // a COMException here ("The operation identifier is not valid. The WinUI Desktop Window
            // object has already been closed."). WPF's equivalent check (_target.RootVisual.IsAncestorOf(this))
            // is a plain managed reference and never touches live native state; WinUI 3's
            // Window.Content getter, by contrast, reaches into the native Window object and throws
            // once it has already been closed. A LayoutUpdated pass can still be delivered here
            // after Window.Close() begins tearing down its native counterpart but before this
            // control's own Unloaded event has fired to release the thumbnail. Treated the same as
            // "the window is gone": release the thumbnail so no further LayoutUpdated pass repeats
            // this crash, rather than just invalidating arrange (which would retry against a Window
            // that will never respond again).
            Microsoft.UI.Xaml.UIElement? root;
            try
            {
                root = window.Content;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                ReleaseThumbnail();
                return;
            }

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

        // Defensive hardening added during WindowSelectorWindow's live verification (Task 4b.4), NOT
        // a confirmed fix for a specific observed crash: CsWin32's DwmQueryThumbnailSourceSize throws
        // on a failing HRESULT (unlike this file's other DWM calls, which check an explicit int/bool
        // return), and this call was previously unguarded. A COMException (E_FAIL, 0x80004005) WAS
        // observed live, from a FrameworkElement's MeasureOverride, under a locked Windows session --
        // but the captured stack trace does not actually show a WindowThumbnail.MeasureOverride frame
        // (it shows the *base* Microsoft.UI.Xaml.FrameworkElement.MeasureOverride delegating straight
        // to native code and failing there), so that specific crash was NOT conclusively attributed to
        // this call -- it most likely originates in a different FrameworkElement in the same tree
        // (EditableTextBlock or SpatialNavigationListView are the candidates that don't override
        // Measure themselves) or in native/compositor code triggered by the locked session, not here.
        // The guard below is kept anyway on its own merits: an unguarded call that can throw an
        // unhandled COMException out of a layout callback is a real robustness gap regardless of
        // whether it explains the one crash that was actually observed.
        try
        {
            PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);
            double scale = 1;
            if (size.Width > availableSize.Width) scale = availableSize.Width / size.Width;
            if (size.Height > availableSize.Height) scale = Math.Min(scale, availableSize.Height / size.Height);
            return new Size(size.Width * scale, size.Height * scale);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return new Size(0, 0);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Stretch || _thumb == 0)
        {
            return finalSize;
        }

        // Defensive hardening, matching MeasureOverride's guard above -- see that comment for why
        // this is NOT a confirmed fix for the one crash actually observed live.
        try
        {
            PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);
            double scale = finalSize.Width / size.Width;
            scale = Math.Min(scale, finalSize.Height / size.Height);
            return new Size(size.Width * scale, size.Height * scale);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return finalSize;
        }
    }
}
