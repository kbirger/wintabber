using System.Reactive;
using System.Reactive.Linq;

namespace WinTabberUI.ViewModels;

internal static class EventHelper
{

    public static IObservable<TResult> EventOrEmpty<TSource, TEventArgs, TResult>(
    TSource? source,
    Action<Windows.Foundation.TypedEventHandler<TSource, TEventArgs>> addHandler,
    Action<Windows.Foundation.TypedEventHandler<TSource, TEventArgs>> removeHandler,
    Func<IObservable<Unit>, IObservable<TResult>> eventObservable)
    {
        if (source is null)
        {
            return Observable.Empty<TResult>();
        }


        var obs = Observable.FromEvent<Windows.Foundation.TypedEventHandler<TSource, TEventArgs>, TEventArgs>(
            handler =>
            {
                Windows.Foundation.TypedEventHandler<TSource, TEventArgs> typedHandler = (sender, e) => { handler(e); };

                return typedHandler;
            },
            addHandler,
            removeHandler)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);
        return eventObservable(obs);
    }
}