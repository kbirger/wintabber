using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;

namespace WinTabber.Api.Media.CoreAudio;

internal static class DeviceGoneExtensions
{
    /// <summary>
    /// Ends the sequence quietly when the device or session behind it is gone. NAudio reports that
    /// as COMException (for example 0x88890004, device invalidated) or, for HRESULT 0x80070002, as
    /// FileNotFoundException. Any other error still flows to the subscriber.
    /// </summary>
    public static IObservable<T> IgnoreDeviceGone<T>(this IObservable<T> source) =>
        source
            .Catch<T, COMException>(ex => Gone<T>(ex))
            .Catch<T, FileNotFoundException>(ex => Gone<T>(ex));

    private static IObservable<T> Gone<T>(Exception ex)
    {
        Debug.WriteLine($"Audio device or session is gone: {ex.Message}");
        return Observable.Empty<T>();
    }
}
