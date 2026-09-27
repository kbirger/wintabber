using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Common.Tests.AccessKeys;

public class AccessKeyBadgeLayerTests
{
    [Test]
    public async Task FindNewPopup_returns_the_one_popup_absent_from_before()
    {
        var p1 = new object();
        var p2 = new object();
        var p3 = new object();

        var before = new[] { p1, p2 };
        var after = new[] { p1, p2, p3 };

        var result = AccessKeyBadgeLayer.FindNewPopup(before, after);

        await Assert.That(result).IsSameReferenceAs(p3);
    }

    [Test]
    public async Task FindNewPopup_returns_null_when_nothing_new()
    {
        var p1 = new object();
        var before = new[] { p1 };
        var after = new[] { p1 };

        var result = AccessKeyBadgeLayer.FindNewPopup(before, after);

        await Assert.That(result).IsNull();
    }
}
