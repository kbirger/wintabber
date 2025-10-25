using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.Diagnostics;
namespace WinTabber.Events
{
    public class ProcessMonitor
    {

        public static void ListenForProcessStart()
        {
            string query = "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";
            var startEventWatcher = new ManagementEventWatcher(query);
            startEventWatcher.EventArrived += (sender, e) =>
            {
                //(ManagementBaseObject)
                var processName = e.NewEvent.Properties["ProcessName"].Value;
                var processId = e.NewEvent.Properties["ProcessId"].Value;
                Debug.WriteLine($"Process Started: {processName} (ID: {processId})");
            };
            startEventWatcher.Start();
        }

        public static void ListenForProcessStop()
        {
            string query = "SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";
            var stopEventWatcher = new ManagementEventWatcher(query);
            stopEventWatcher.EventArrived += (sender, e) =>
            {
                var processName = e.NewEvent.Properties["ProcessName"].Value;
                var processId = e.NewEvent.Properties["ProcessId"].Value;
                Debug.WriteLine($"Process Stopped: {processName} (ID: {processId})");
            };
            stopEventWatcher.Start();
        }
    }
}
