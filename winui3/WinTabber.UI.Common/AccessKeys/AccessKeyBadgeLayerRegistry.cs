namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Maps a key (a window's XamlRoot in production) to its AccessKeyBadgeLayer, so an attached
/// property on an arbitrary descendant element can find "this element's window's layer" without
/// a visual-tree walk. Generic over both type parameters so it is headless-testable with plain
/// objects, matching AccessKeyBadge.ComputeUnderlineLength's precedent of pulling pure logic out
/// of the WinUI-dependent classes around it.
/// </summary>
public sealed class AccessKeyBadgeLayerRegistry<TKey, TLayer>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TLayer> _byKey = new();

    public void Register(TKey key, TLayer layer) => _byKey[key] = layer;

    public void Unregister(TKey key) => _byKey.Remove(key);

    public bool TryGet(TKey key, out TLayer layer) => _byKey.TryGetValue(key, out layer!);
}
