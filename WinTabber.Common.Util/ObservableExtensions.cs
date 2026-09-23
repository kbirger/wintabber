using System.Reactive.Linq;

namespace WinTabber.Common.Util;

public static class ObservableExtensions
{
    extension<TSource>(IObservable<TSource> source)
    {
        public IObservable<TResult> ExhaustMap<TResult>(Func<TSource, IObservable<TResult>> function)
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

    extension<TSource>(IObservable<IObservable<TSource>?> source)
    {
        public IObservable<IObservable<TSource>> OrDefault(TSource defaultValue)
        {
            return source.Select(value => value ?? Observable.Return(defaultValue));
        }
    }

    extension<TToggle>(IObservable<TToggle> source)
    {
        /// <summary>
        /// A bool that flips on every <paramref name="source"/> emission and resets to false on
        /// every <paramref name="resetSource"/> emission, starting false. Element values on both
        /// streams are ignored -- only their timing matters.
        /// </summary>
        public IObservable<bool> ToggleWithReset<TReset>(IObservable<TReset> resetSource)
        {
            var toggles = source.Select(_ => (Func<bool, bool>)(isOn => !isOn));
            var resets = resetSource.Select(_ => (Func<bool, bool>)(_ => false));
            return toggles.Merge(resets).Scan(false, (isOn, apply) => apply(isOn)).StartWith(false);
        }
    }
}
