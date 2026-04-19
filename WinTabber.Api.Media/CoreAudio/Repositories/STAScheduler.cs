using System.Diagnostics;
using System.Reactive.Concurrency;

namespace WinTabber.Api.Media.CoreAudio.Repositories;

public static class STAScheduler
{
    public const string Key = "STAScheduler";

    public static EventLoopScheduler Create()
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
}
