using System.Collections.Concurrent;

namespace WinTabberUI.Interop;

public sealed class StaThreadHost : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _queue = new();

    public StaThreadHost()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        // Initialize COM implicitly via STA
        while (_queue.TryTake(out var action, Timeout.Infinite))
        {
            action();
        }
    }

    public void Invoke(Action action)
    {
        using var done = new ManualResetEventSlim();
        _queue.Add(() =>
        {
            try { action(); }
            finally { done.Set(); }
        });
        done.Wait();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
    }
}

