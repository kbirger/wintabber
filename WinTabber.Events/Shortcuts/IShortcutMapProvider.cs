using System.Reactive.Subjects;

namespace WinTabber.Events.Shortcuts;

/// <summary>
/// The live keymap. The detection layer subscribes to <see cref="Maps" /> and rebinds on every
/// emission; the settings UI pushes a new map on save.
/// </summary>
public interface IShortcutMapProvider
{
    ShortcutMap Current { get; }

    /// <summary>Replays the current map on subscribe, then every subsequent replacement.</summary>
    IObservable<ShortcutMap> Maps { get; }

    void Update(ShortcutMap map);
}

public sealed class ShortcutMapProvider : IShortcutMapProvider, IDisposable
{
    private readonly BehaviorSubject<ShortcutMap> _maps;

    public ShortcutMapProvider()
        : this(ShortcutMap.Default) { }

    public ShortcutMapProvider(ShortcutMap initial)
    {
        _maps = new BehaviorSubject<ShortcutMap>(initial);
    }

    public ShortcutMap Current => _maps.Value;

    public IObservable<ShortcutMap> Maps => _maps;

    public void Update(ShortcutMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _maps.OnNext(map);
    }

    public void Dispose() => _maps.Dispose();
}
