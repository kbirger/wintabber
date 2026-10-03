using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace WinTabberUI.Services;

/// <summary>
/// Holds the on/off state of the media debug view. The tray menu writes it and
/// MediaDebugWindowCoordinator reads it.
/// </summary>
/// <remarks>
/// The debug window cannot open on its own without starting the media pipelines it is supposed to
/// observe: every source is a lazily created, ref-counted cache, so the first subscriber starts
/// device enumeration and SMTC discovery. The toggle therefore only arms the window. The window
/// then opens and closes with the media controls window, so its subscriptions live exactly as long
/// as the real feature's subscriptions.
/// </remarks>
public class MediaDebugStateService
{
    private readonly BehaviorSubject<bool> _isEnabled = new(false);

    public bool IsEnabled => _isEnabled.Value;

    public IObservable<bool> IsEnabledChanges => _isEnabled.AsObservable();

    public void Toggle()
    {
        _isEnabled.OnNext(!_isEnabled.Value);
    }
}
