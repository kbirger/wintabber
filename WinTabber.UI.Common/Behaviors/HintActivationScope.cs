using System.Windows;

namespace WinTabber.UI.Common.Behaviors;

/// <summary>
/// Tracks which single root element currently owns the hint overlay.
/// </summary>
/// <remarks>
/// The arbitration really is app-wide — only one root shows hints at a time — so the state is
/// shared by design and this type does not try to eliminate the sharing. What it changes is the
/// <em>lifetime</em>. The state used to be a bare <c>static</c> field on <see cref="HintBehavior"/>,
/// which made its scope the process: a root activated once stayed recorded forever, so in a test
/// run the second test to activate a root would find the first test's root still owning the
/// overlay and silently do nothing (see <c>HintBehavior.OnTriggerKeyDown</c>, which returns early
/// when the active root is not the element being asked about). Making the scope an object that
/// <see cref="HintBehavior.ActivationScope"/> points at keeps one arbiter at runtime while letting
/// a test install a fresh one per test.
///
/// The reference to the root stays weak, as it was before: the overlay owner is not something this
/// type should keep alive, and a strong reference here would pin a whole visual tree.
/// </remarks>
internal sealed class HintActivationScope
{
    private WeakReference<FrameworkElement>? _activeRootRef;

    /// <summary>
    /// The root element currently owning the hint overlay, or <see langword="null"/> if no root
    /// owns it — including the case where the previous owner has been collected.
    /// </summary>
    public FrameworkElement? ActiveRoot
    {
        get => _activeRootRef is not null && _activeRootRef.TryGetTarget(out var root) ? root : null;
        set
        {
            if (value is null)
            {
                _activeRootRef = null;
            }
            else if (_activeRootRef is not null)
            {
                _activeRootRef.SetTarget(value);
            }
            else
            {
                _activeRootRef = new WeakReference<FrameworkElement>(value);
            }
        }
    }
}
