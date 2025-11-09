// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Threading.Tasks;
// using System.Management;
// using System.Diagnostics;
// using System.Reactive.Linq;
// namespace WinTabber.Events
// {
//     public class ProcessMonitor
//     {


//         public static IObservable<IReadOnlyList<Process>> CreateObservable(TimeSpan fullRefreshInterval)
//         {
//             // Full snapshot occasionally
//             var fullRefreshes =
//                 Observable.Interval(fullRefreshInterval)
//                           .StartWith(0L)
//                           .Select(_ => new ProcessChange
//                           {
//                               Type = ChangeType.FullRefresh,
//                               Snapshot = Process.GetProcesses()
//                           });

//             // Process start event (new process)
//             var processStarts = Observable.Using(
//                 () => new ManagementEventWatcher("SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'"),
//                 watcher => Observable.FromEventPattern<EventArrivedEventHandler, EventArrivedEventArgs>(
//                         h => watcher.EventArrived += h,
//                         h => watcher.EventArrived -= h)
//                     .Do(_ => watcher.Start())
//                     .Select(e =>
//                     {
//                         var processName = e.EventArgs.NewEvent.Properties["ProcessName"].Value;
//                         var processId = e.EventArgs.NewEvent.Properties["ProcessId"].Value;
//                         return new ProcessChange
//                         {
//                             Type = ChangeType.Start,
//                             ProcessId = Convert.ToInt32(processId),
//                             ProcessName = processName?.ToString()
//                         };
//                     })
//                     .Finally(() => watcher.Stop())
//             );

//             // Process stop event (process exited)
//             var processStops = Observable.Using(
//                 () => new ManagementEventWatcher("SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'"),
//                 watcher => Observable.FromEventPattern<EventArrivedEventHandler, EventArrivedEventArgs>(
//                         h => watcher.EventArrived += h,
//                         h => watcher.EventArrived -= h)
//                     .Do(_ => watcher.Start())
//                     .Select(e =>
//                     {
//                         var processName = e.EventArgs.NewEvent.Properties["ProcessName"].Value;
//                         var processId = e.EventArgs.NewEvent.Properties["ProcessId"].Value;
//                         return new ProcessChange
//                         {
//                             Type = ChangeType.Stop,
//                             ProcessId = Convert.ToInt32(processId),
//                             ProcessName = processName?.ToString()
//                         };
//                     })
//                     .Finally(() => watcher.Stop())
//             );

//             // Combine all change sources
//             var allChanges = Observable.Merge(fullRefreshes, processStarts, processStops);

//             // Maintain state using Scan
//             var processList = allChanges.Scan(new Dictionary<int, Process>(), (current, change) =>
//             {
//                 switch (change.Type)
//                 {
//                     case ChangeType.FullRefresh:
//                         // Replace everything with the new snapshot
//                         foreach (var p in current.Values) p.Dispose();
//                         return change.Snapshot.ToDictionary(p => p.Id, p => p);

//                     case ChangeType.Start:
//                         try
//                         {
//                             var proc = Process.GetProcessById(change.ProcessId);
//                             current[proc.Id] = proc;
//                         }
//                         catch { /* process exited before we fetched */ }
//                         return current;

//                     case ChangeType.Stop:
//                         if (current.Remove(change.ProcessId, out var exited))
//                             exited.Dispose();
//                         return current;

//                     default:
//                         return current;
//                 }
//             })
//             .Select(dict => (IReadOnlyList<Process>)dict.Values.ToList())
//             .Replay(1)
//             .RefCount();

//             return processList;
//         }

//         private enum ChangeType { FullRefresh, Start, Stop }

//         private class ProcessChange
//         {
//             public ChangeType Type { get; set; }
//             public Process[] Snapshot { get; set; }
//             public int ProcessId { get; set; }
//             public string? ProcessName { get; internal set; }
//         }
//         public static void ListenForProcessStart()
//         {
//             string query = "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";
//             var startEventWatcher = new ManagementEventWatcher(query);
//             startEventWatcher.EventArrived += (sender, e) =>
//             {
//                 //(ManagementBaseObject)
//                 var processName = e.NewEvent.Properties["ProcessName"].Value;
//                 var processId = e.NewEvent.Properties["ProcessId"].Value;
//                 Debug.WriteLine($"Process Started: {processName} (ID: {processId})");
//             };
//             startEventWatcher.Start();
//         }

//         public static void ListenForProcessStop()
//         {
//             string query = "SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";
//             var stopEventWatcher = new ManagementEventWatcher(query);
//             stopEventWatcher.EventArrived += (sender, e) =>
//             {
//                 var processName = e.NewEvent.Properties["ProcessName"].Value;
//                 var processId = e.NewEvent.Properties["ProcessId"].Value;
//                 Debug.WriteLine($"Process Stopped: {processName} (ID: {processId})");
//             };
//             stopEventWatcher.Start();
//         }
//     }
// }
