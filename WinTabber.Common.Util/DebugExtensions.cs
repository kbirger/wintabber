using System.Diagnostics;
using System.Reactive.Linq;

namespace WinTabber.Common.Util;

public static class DebugExtensions
{
    extension<T>(IObservable<T> observable)
    {
        public IObservable<T> Log(Func<T, string> message)
        {
            return observable.Do(value =>
            {
                Debug.WriteLine(message(value));
            });
        }
    }
}
