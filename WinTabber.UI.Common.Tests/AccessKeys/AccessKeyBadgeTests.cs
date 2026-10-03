using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Common.Tests.AccessKeys;

public class AccessKeyBadgeTests
{
    [Test]
    public async Task ComputeUnderlineLength_ReturnsZero_WhenNoKeysPressedYet()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("BC", "");

        await Assert.That(length).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeUnderlineLength_ReturnsPressedLength_WhenPressedKeysIsAPrefix()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("BC", "B");

        await Assert.That(length).IsEqualTo(1);
    }

    [Test]
    public async Task ComputeUnderlineLength_ReturnsFullLength_WhenPressedKeysMatchesExactly()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("P", "P");

        await Assert.That(length).IsEqualTo(1);
    }

    [Test]
    public async Task ComputeUnderlineLength_ReturnsNegativeOne_WhenPressedKeysDoesNotMatch()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("P", "B");

        await Assert.That(length).IsEqualTo(-1);
    }

    [Test]
    public async Task ComputeUnderlineLength_IsCaseInsensitive()
    {
        var length = AccessKeyBadge.ComputeUnderlineLength("BC", "b");

        await Assert.That(length).IsEqualTo(1);
    }
}
