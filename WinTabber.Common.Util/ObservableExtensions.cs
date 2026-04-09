using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Common.Util;

public static class ObservableExtensions
{
    public static IObservable<TResult> ExhaustMap<TSource, TResult>(
    this IObservable<TSource> source,
    Func<TSource, IObservable<TResult>> function)
    {
        return Observable.Defer(() =>
        {
            int mutex = 0; // 0: not acquired, 1: acquired
            return source.SelectMany(item =>
            {
                // Attempt to acquire the mutex immediately. If successful, return
                // a sequence that releases the mutex when terminated. Otherwise,
                // return immediately an empty sequence.
                if (Interlocked.CompareExchange(ref mutex, 1, 0) == 0)
                    return function(item).Finally(() => Volatile.Write(ref mutex, 0));
                return Observable.Empty<TResult>();
            });
        });
    }
}
