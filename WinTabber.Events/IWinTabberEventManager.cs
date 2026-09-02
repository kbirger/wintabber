namespace WinTabber.Events;

public interface IWinTabberEventManager : IDisposable
{
    IObservable<WinTabberEvent<string>> ApplicationChange { get; }
    IObservable<WinTabberEvent> CommandEvents { get; }
    IObservable<WinTabberEvent<int>> WindowChange { get; }

    /// <summary>
    /// Raw foreground-window handles, with repeated values kept. Use this to detect a return to
    /// the window the user came from, which <see cref="WindowChange" /> removes.
    /// </summary>
    IObservable<int> ForegroundWindowChanges { get; }

    void Pause();
    void Start();
    void SendEvent(WinTabberEvent evt);
}