using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TUnit.Core.Executors;
using WinTabber.UI.Common.Behaviors;

namespace WinTabber.UI.Common.Tests.Behaviors;

/// <summary>
/// Tier 2: these need a real presentation source, i.e. an actual <see cref="Window.Show"/>.
/// </summary>
/// <remarks>
/// Everything reachable without showing a window lives in <see cref="HintBehaviorTests"/>. The
/// split is not cosmetic — it is the difference between what any agent can run and what needs a
/// desktop. Two independent facts force it, both established by measurement rather than assumed:
/// <list type="bullet">
/// <item><description>A <see cref="Window"/> has no visual child until it has a presentation
/// source, so <c>HintBehavior.GetAttachedElements</c> walking from an unshown window finds
/// nothing, no matter how much you <c>Measure</c>/<c>Arrange</c> it.</description></item>
/// <item><description><c>DefaultHintBehaviorKernel.GetAttachableElements</c> filters on
/// <c>FrameworkElement.IsLoaded</c>, which stays false until the same thing happens.</description></item>
/// </list>
/// These pass locally. Whether a headless CI runner tolerates <c>Show()</c> was not verified when
/// they were written, hence the category: exclude with
/// <c>--treenode-filter "/*/*/*/*[Category!=RequiresDesktop]"</c> and the tier-1 coverage,
/// including everything guarding the activation scope, still runs.
/// </remarks>
[Category("RequiresDesktop")]
[NotInParallel]
public class HintBehaviorDesktopTests
{
    private static Window BuildShownRoot(out Button hinted)
    {
        hinted = new Button { Content = "target" };
        var unhinted = new Button { Content = "no hint" };
        var grid = new Grid();
        grid.Children.Add(hinted);
        grid.Children.Add(unhinted);

        var window = new Window
        {
            Content = new AdornerDecorator { Child = grid },
            Width = 200,
            Height = 200,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
        };
        return window;
    }

    [Test]
    [STAThreadExecutor]
    public async Task GetAttachedElements_FindsHintedDescendants_OfAShownWindow()
    {
        var window = BuildShownRoot(out var hinted);
        HintBehavior.SetHintText(hinted, "a");

        window.Show();
        try
        {
            var found = HintBehavior.GetAttachedElements(window).ToList();

            await Assert.That(found).Contains(hinted);
            await Assert.That(found).Count().IsEqualTo(1);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [STAThreadExecutor]
    public async Task DefaultKernel_GetAttachableElements_RequiresTheElementToBeLoaded()
    {
        var window = BuildShownRoot(out var hinted);
        HintBehavior.SetHintText(hinted, "a");
        var kernel = new DefaultHintBehaviorKernel();

        // The kernel filters on IsLoaded, so before Show() there is nothing to attach to.
        await Assert.That(hinted.IsLoaded).IsFalse();
        await Assert.That(kernel.GetAttachableElements(window)).IsEmpty();

        window.Show();
        try
        {
            await Assert.That(hinted.IsLoaded).IsTrue();
            await Assert.That(kernel.GetAttachableElements(window)).Contains(hinted);
        }
        finally
        {
            window.Close();
        }
    }
}
