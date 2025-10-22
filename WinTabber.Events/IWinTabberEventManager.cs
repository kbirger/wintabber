using WinTabber.Interop;

namespace WinTabber.Events
{
    public interface IWinTabberEventManager : IDisposable
    {
        IObservable<WinTabberEvent<string>> ApplicationChange { get; }
        IObservable<WinTabberEvent> CommandEvents { get; }
        IObservable<WinTabberEvent<int>> WindowChange { get; }

        void SendEvent(WinTabberEvent evt);
    }
}