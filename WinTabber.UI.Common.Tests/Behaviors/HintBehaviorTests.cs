using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Xaml.Behaviors;
using TUnit.Core.Executors;
using WinTabber.UI.Common.Behaviors;

namespace WinTabber.UI.Common.Tests.Behaviors;

/// <summary>
/// Tier 1: no <see cref="Window.Show"/>, so these run anywhere an STA thread is available.
/// Anything needing a real presentation source lives in <see cref="HintBehaviorDesktopTests"/>
/// and carries the <c>RequiresDesktop</c> category.
/// </summary>
/// <remarks>
/// <c>NotInParallel</c> is load-bearing: these swap <see cref="HintBehavior.ActivationScope"/>,
/// which is one shared seam, so running them concurrently would reintroduce exactly the
/// cross-test interference the scope exists to remove.
/// </remarks>
[NotInParallel]
public class HintBehaviorTests
{
    /// <summary>
    /// Every test gets its own activation scope. Without this the arbitration state leaks from
    /// one test into the next and test order starts to matter -- which is the whole point of
    /// T6.4. Kept here rather than in a base class so the isolation is visible at the use site.
    /// </summary>
    private static HintActivationScope FreshScope()
    {
        var scope = new HintActivationScope();
        HintBehavior.ActivationScope = scope;
        return scope;
    }

    private static Window BuildRoot(out Button hinted)
    {
        hinted = new Button { Content = "target" };
        var grid = new Grid();
        grid.Children.Add(hinted);
        var window = new Window { Content = new AdornerDecorator { Child = grid } };
        window.Measure(new Size(800, 600));
        window.Arrange(new Rect(0, 0, 800, 600));
        return window;
    }

    [Test]
    [STAThreadExecutor]
    public async Task ShowingHints_RecordsTheRootInTheActivationScope()
    {
        var scope = FreshScope();
        var window = BuildRoot(out _);
        var behavior = new HintBehavior();
        Interaction.GetBehaviors(window).Add(behavior);

        await Assert.That(scope.ActiveRoot).IsNull();

        behavior.AreHintsShown = true;

        await Assert.That(scope.ActiveRoot).IsSameReferenceAs(window);
    }

    [Test]
    [STAThreadExecutor]
    public async Task HidingHints_ClearsTheActivationScope()
    {
        var scope = FreshScope();
        var window = BuildRoot(out _);
        var behavior = new HintBehavior();
        Interaction.GetBehaviors(window).Add(behavior);

        behavior.AreHintsShown = true;
        behavior.AreHintsShown = false;

        await Assert.That(scope.ActiveRoot).IsNull();
    }

    /// <summary>
    /// The regression that made T6.4 worth doing: with a process-lifetime static, a root
    /// activated by one test stays active for the next one, and the guard in OnTriggerKeyDown
    /// then silently refuses to show hints for the new root.
    /// </summary>
    [Test]
    [STAThreadExecutor]
    public async Task ReplacingTheScope_DiscardsAPreviouslyActivatedRoot()
    {
        var first = FreshScope();
        var windowA = BuildRoot(out _);
        var behaviorA = new HintBehavior();
        Interaction.GetBehaviors(windowA).Add(behaviorA);
        behaviorA.AreHintsShown = true;
        await Assert.That(first.ActiveRoot).IsSameReferenceAs(windowA);

        // Simulates the next test starting.
        var second = FreshScope();

        await Assert.That(second.ActiveRoot).IsNull();
        await Assert.That(second).IsNotSameReferenceAs(first);
    }

    /// <summary>
    /// Runtime semantics must not change: the arbitration is still app-wide, so activating a
    /// second root within one scope hands ownership over rather than letting both stay active.
    /// </summary>
    [Test]
    [STAThreadExecutor]
    public async Task ActivatingASecondRoot_TakesOwnershipFromTheFirst()
    {
        var scope = FreshScope();
        var windowA = BuildRoot(out _);
        var behaviorA = new HintBehavior();
        Interaction.GetBehaviors(windowA).Add(behaviorA);

        var windowB = BuildRoot(out _);
        var behaviorB = new HintBehavior();
        Interaction.GetBehaviors(windowB).Add(behaviorB);

        behaviorA.AreHintsShown = true;
        behaviorB.AreHintsShown = true;

        await Assert.That(scope.ActiveRoot).IsSameReferenceAs(windowB);
    }

    [Test]
    [STAThreadExecutor]
    public async Task ActivationScope_DoesNotKeepTheRootAlive()
    {
        var scope = new HintActivationScope();

        static WeakReference Activate(HintActivationScope target)
        {
            var window = new Window();
            target.ActiveRoot = window;
            return new WeakReference(window);
        }

        var probe = Activate(scope);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The scope holds a weak reference, so a collected root reads back as null rather than
        // pinning a whole visual tree for the life of the process.
        await Assert.That(probe.IsAlive).IsFalse();
        await Assert.That(scope.ActiveRoot).IsNull();
    }

    [Test]
    [STAThreadExecutor]
    public async Task SetHintText_OnElementWithNoWindowAncestor_DoesNotThrow()
    {
        // OnHintTextChanged calls Window.GetWindow(d), which returns null for an unrooted
        // element, and passed that null straight into Interaction.GetBehaviors.
        var button = new Button { Content = "target" };
        var grid = new Grid();
        grid.Children.Add(button);

        await Assert.That(() => HintBehavior.SetHintText(button, "a")).ThrowsNothing();
        await Assert.That(HintBehavior.GetHintText(button)).IsEqualTo("a");
    }
}
