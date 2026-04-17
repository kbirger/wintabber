using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace WinTabberUI.Helpers;

[ComImport]
[Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICompositorDesktopInterop
{
    void CreateDesktopWindowTarget(IntPtr hwndTarget, bool isTopmost, out IntPtr result);
    void EnsureOnThread(uint threadId);
}

internal sealed class CompositionWindowAnimator
{
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;
    private SpriteVisual? _overlay;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        internal int dwSize;
        internal int threadType;
        internal int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        out IntPtr dispatcherQueueController);

    private static IntPtr _dispatcherQueueController;

    private static void EnsureDispatcherQueue()
    {
        if (_dispatcherQueueController != IntPtr.Zero)
            return;

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,    // DQTYPE_THREAD_CURRENT
            apartmentType = 2, // DQTAT_COM_STA
        };
        CreateDispatcherQueueController(options, out _dispatcherQueueController);
    }

    private void Initialize(Window window)
    {
        if (_compositor is not null)
            return;

        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        EnsureDispatcherQueue();
        _compositor = new Compositor();

        var interop = _compositor.As<ICompositorDesktopInterop>();
        interop.CreateDesktopWindowTarget(hwnd, true, out var rawTarget);
        _target = MarshalInterface<DesktopWindowTarget>.FromAbi(rawTarget);

        _root = _compositor.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;
        _target.Root = _root;

        _overlay = _compositor.CreateSpriteVisual();
        _overlay.Brush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0xFF, 0, 0, 0));
        _overlay.RelativeSizeAdjustment = Vector2.One;
        _overlay.Opacity = 0f;

        _root.Children.InsertAtTop(_overlay);
    }

    public void AnimateShow(Window window, double durationMs = 500)
    {
        Initialize(window);

        _overlay!.Opacity = 1f;
        window.Show();

        var anim = _compositor!.CreateScalarKeyFrameAnimation();
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(1f, 0f, _compositor.CreateCubicBezierEasingFunction(new Vector2(0.0f, 0.0f), new Vector2(0.4f, 1.0f)));
        _overlay.StartAnimation("Opacity", anim);
    }

    public void AnimateHide(Window window, double durationMs = 150, Action? onComplete = null)
    {
        Initialize(window);

        var batch = _compositor!.CreateScopedBatch(CompositionBatchTypes.Animation);

        batch.Completed += (s, e) =>
        {
            window.Dispatcher.Invoke(() =>
            {
                window.Hide();
                _overlay!.Opacity = 0f;
                onComplete?.Invoke();
            });
        };

        var anim = _compositor.CreateScalarKeyFrameAnimation();
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        anim.InsertKeyFrame(0f, 0f);
        anim.InsertKeyFrame(1f, 1f, _compositor.CreateCubicBezierEasingFunction(new Vector2(0.6f, 0.0f), new Vector2(1.0f, 1.0f)));
        _overlay!.StartAnimation("Opacity", anim);

        batch.End();
    }
}
