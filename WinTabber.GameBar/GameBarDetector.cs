namespace WinTabber.GameBar;

using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Windows.Gaming.UI;

public class GameBarDetector
{
    private static IObservable<bool> _gameBarVisibility;
    static GameBarDetector()
    {
        //GameBar.VisibilityChanged += GameBar_VisibilityChanged;
        //_gameBarVisibility = Observable
        //    .FromEventPattern(typeof(GameBar), nameof(GameBar.VisibilityChanged))
        //    .Select(_ => GameBar.Visible);

        _gameBarVisibility = Observable.Interval(TimeSpan.FromMilliseconds(100))
            .ObserveOn(new EventLoopScheduler())
            .Select(_ => GameBar.IsInputRedirected)
            .DistinctUntilChanged();

    }


    public static IObservable<bool> GameBarVisibility => _gameBarVisibility;
}
