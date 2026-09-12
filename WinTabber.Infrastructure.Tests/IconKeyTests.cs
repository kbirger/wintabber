using WinTabber.Events.Shortcuts;
using WinTabber.Infrastructure;
using WinTabber.Infrastructure.Settings;

namespace WinTabber.Infrastructure.Tests;

public class IconKeyTests
{
    [Test]
    public async Task GetIcon_ReturnsIconKeyMatchingCatalogJson()
    {
        var icon = ShortcutCommand.NextWindow.GetIcon();

        await Assert.That(icon).IsEqualTo(IconKey.ArrowNext_24_Filled);
    }

    [Test]
    public async Task GetIcon_ThrowsForEveryBindableCommand()
    {
        // Every command in ShortcutCommandExtensions.Bindable must resolve to a real IconKey —
        // this is the same guarantee ShortcutCommandCatalog.For() gave before the IconKey change,
        // just asserted directly instead of only failing lazily the first time a page renders.
        foreach (var command in ShortcutCommandExtensions.Bindable)
        {
            var icon = command.GetIcon();
            await Assert.That(Enum.IsDefined(icon)).IsTrue();
        }
    }
}
