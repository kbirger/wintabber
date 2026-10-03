using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Common.Tests.AccessKeys;

public class AccessKeyBadgeLayerRegistryTests
{
    [Test]
    public async Task TryGet_ReturnsRegisteredLayer()
    {
        var registry = new AccessKeyBadgeLayerRegistry<string, object>();
        var layer = new object();
        registry.Register("key", layer);

        var found = registry.TryGet("key", out var result);

        await Assert.That(found).IsTrue();
        await Assert.That(result).IsEqualTo(layer);
    }

    [Test]
    public async Task TryGet_ReturnsFalse_WhenKeyNotRegistered()
    {
        var registry = new AccessKeyBadgeLayerRegistry<string, object>();

        var found = registry.TryGet("missing", out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task TryGet_ReturnsFalse_AfterUnregister()
    {
        var registry = new AccessKeyBadgeLayerRegistry<string, object>();
        registry.Register("key", new object());

        registry.Unregister("key");
        var found = registry.TryGet("key", out _);

        await Assert.That(found).IsFalse();
    }
}
