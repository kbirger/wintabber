// using System;
// using System.Collections.Generic;
// using System.Diagnostics;
// using System.Drawing.Text;
// using System.Linq;
// using System.Reactive;
// using System.Reactive.Linq;
// using System.Reactive.Subjects;
// using System.Text;
// using System.Threading.Tasks;

// namespace WinTabber.API;

// internal static class ProcessMonitor
// {
//     private static Subject<Unit> _refresh = new Subject<Unit>();

//     //private static readonly IDisposable _anchor =
    
//     static ProcessMonitor()
//     {
//         Processes.Subscribe(_ => { }); // keep it hot
//     }

//     public static IDisposable RefreshOn(IObservable<Unit> trigger)
//     {
//         return trigger.Subscribe(_ => _refresh.OnNext(Unit.Default));
//     }

//     private static IObservable<Unit> _refreshTriggers = Observable.Merge(
//             _refresh,
//             Observable.Interval(TimeSpan.FromSeconds(1)).Select(_ => Unit.Default)
//         ).Throttle(TimeSpan.FromSeconds(1));

//     public static IObservable<ILookup<string, Process>> Processes { get; } =
//         _refreshTriggers
//         .StartWith(Unit.Default)
//         .Select(_ => Observable.FromAsync(() => Task.Run(GetData)))
//         .Switch()
//         .Replay(1) // cache the last computed data
//         .RefCount();

//     private static ILookup<string, Process> GetData()
//     {
//         return Process
//             .GetProcesses()
//             .ToLookup(p => p.ProcessName, StringComparer.OrdinalIgnoreCase);
//     }
// }
