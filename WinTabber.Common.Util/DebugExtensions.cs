using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Text;

namespace WinTabberUI;

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
