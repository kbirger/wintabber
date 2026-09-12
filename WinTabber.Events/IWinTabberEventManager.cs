using WinTabber.Events.Shortcuts;

namespace WinTabber.Events;

public interface IWinTabberEventManager : IDisposable
{
    IObservable<WinTabberEvent<string>> ApplicationChange { get; }
    IObservable<WinTabberEvent> CommandEvents { get; }
    IObservable<WinTabberEvent<int>> WindowChange { get; }

    /// <summary>
    /// The live modifier keys currently held, for callers that need a synchronous read at the
    /// moment of an action (e.g. Focus Select checking whether its modifier is down when a
    /// selection commits) rather than a subscription.
    /// </summary>
    ShortcutModifiers HeldModifiers { get; }

    /// <summary>
    /// Raw foreground-window handles, with repeated values kept. Use this to detect a return to
    /// the window the user came from, which <see cref="WindowChange" /> removes.
    /// </summary>
    IObservable<int> ForegroundWindowChanges { get; }

    void Pause();
    void Start();
    void SendEvent(WinTabberEvent evt);
}