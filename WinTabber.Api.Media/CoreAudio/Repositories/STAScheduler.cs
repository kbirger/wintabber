using System.Diagnostics;
using System.Reactive.Concurrency;

namespace WinTabber.Api.Media.CoreAudio.Repositories;

public static class STAScheduler
{
    private static readonly EventLoopScheduler _instance;

    static STAScheduler()
    {
        _instance = GetScheduler();
    }

    private static EventLoopScheduler GetScheduler()
    {
        return new EventLoopScheduler(ts =>
        {
            var thread = new Thread(ts) { IsBackground = true };
            Debug.WriteLine($"Creating eventloop STA thread: {thread.ManagedThreadId}");
            thread.Name = "CoreAudioWorker";
            thread.SetApartmentState(ApartmentState.STA);
            return thread;
        });
    }


    public static EventLoopScheduler Default => _instance;
}
