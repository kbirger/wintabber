using WinTabber.Interop;

namespace WinTabber.Events
{
    public interface IWinTabberEventManager : IDisposable
    {
        IObservable<WinTabberEvent<string>> ApplicationChange { get; }
        IObservable<WinTabberEvent> CommandEvents { get; }
        IObservable<WinTabberEvent<ActiveWindowChangeData>> WindowChange { get; }
        IObservable<WinTabberEvent<bool>> GameBarVisibilityChange { get; }

        void SendEvent(WinTabberEvent evt);
    }
}