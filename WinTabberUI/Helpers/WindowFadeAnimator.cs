using System.Windows;
using System.Windows.Media.Animation;

namespace WinTabberUI.Helpers;

/// <summary>
/// Fades a window in and out with its own opacity.
/// </summary>
/// <remarks>
/// This works only on a window with AllowsTransparency set, because WPF then owns the per pixel
/// alpha of the window. The DWM blur behind the window is drawn by the system and does not fade
/// with it.
/// </remarks>
internal sealed class WindowFadeAnimator
{
    // The window is invisible for the first frames of a show, so a slower curve there hides the
    // moment the window appears. The hide is shorter, because a slow disappearance feels sluggish.
    private static readonly KeySpline ShowEase = new(0.0, 0.0, 0.4, 1.0);
    private static readonly KeySpline HideEase = new(0.6, 0.0, 1.0, 1.0);

    // A hide animation hides the window in a completion callback. A show that starts before the
    // callback runs increments this counter, and the stale callback then does nothing.
    private int _generation;

    public void AnimateShow(Window window, double durationMs = 200)
    {
        _generation++;

        if (!window.IsVisible)
        {
            // The window must be invisible before it appears. A held animation value beats a plain
            // assignment, so the animation has to be cleared first.
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 0;
            window.Show();
        }

        // No key frame at zero: the animation then starts from the current opacity, so a show
        // during a hide does not jump.
        Fade(window, 1, durationMs, ShowEase, null);
    }

    public void AnimateHide(Window window, double durationMs = 150, Action? onComplete = null)
    {
        var generation = ++_generation;

        Fade(
            window,
            0,
            durationMs,
            HideEase,
            () =>
            {
                if (_generation != generation)
                {
                    // A show started during the hide animation. The window must stay visible.
                    return;
                }

                window.Hide();
                onComplete?.Invoke();
            }
        );
    }

    private static void Fade(Window window, double to, double durationMs, KeySpline ease, Action? onComplete)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration };
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), ease));

        if (onComplete is not null)
            animation.Completed += (_, _) => onComplete();

        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
