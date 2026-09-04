using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System.Security.Principal;

namespace WinTabberUI.Services;

public class AutoStartupService
{
    private const string StartupPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string LogonTaskName = $"WinTabber Startup";
    private const string LogonTaskDesc = $"WinTabber Auto Startup";

    public void EnsureStartupMode(StartupMode expectedStartupMode)
    {
        // We need to check both because if both of them are enabled,
        // Hide Flow Launcher on startup will not work since the later one will trigger main window show event
        var (logonTaskEnabled, registryEnabled) = ModeToValues(GetMode());
        if (expectedStartupMode == StartupMode.Task)
        {
            // Enable logon task
            if (!logonTaskEnabled)
            {
                ScheduleLogonTask();
            }
            // Disable registry
            if (registryEnabled)
            {
                DisableRegistry();
            }
        }
        else if (expectedStartupMode == StartupMode.Registry)
        {
            // Enable registry
            if (!registryEnabled)
            {
                EnableRegistry();
            }
            // Disable logon task
            if (logonTaskEnabled)
            {
                UnscheduleLogonTask();
            }
        }
        else
        {
            // Disable registry
            if (registryEnabled)
            {
                DisableRegistry();
            }
            // Disable logon task
            if (logonTaskEnabled)
            {
                UnscheduleLogonTask();
            }
        }
    }

    public StartupMode GetMode()
    {
        var logonTaskEnabled = CheckLogonTask();
        var registryEnabled = CheckRegistry();

        return (logonTaskEnabled, registryEnabled) switch
        {
            (true, _) => StartupMode.Task,
            (false, true) => StartupMode.Registry,
            _ => StartupMode.Disabled,
        };
    }

    private static (bool LogonTaskEnabled, bool RegistryEnabled) ModeToValues(StartupMode mode)
    {
        var logonTaskEnabled = CheckLogonTask();
        var registryEnabled = CheckRegistry();
        return (logonTaskEnabled, registryEnabled);
    }

    private static string GetExecutablePath()
    {
        return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    }

    private static bool CheckLogonTask()
    {
        using var taskService = new TaskService();
        var task = taskService.RootFolder.AllTasks.FirstOrDefault(t => t.Name == LogonTaskName);
        if (task != null)
        {
            try
            {
                // Check if the action is the same as the current executable path
                // If not, we need to unschedule and reschedule the task
                if (task.Definition.Actions.FirstOrDefault() is Microsoft.Win32.TaskScheduler.Action taskAction)
                {
                    var action = taskAction.ToString().Trim();
                    if (!action.Equals(GetExecutablePath(), StringComparison.OrdinalIgnoreCase))
                    {
                        UnscheduleLogonTask();
                        ScheduleLogonTask();
                    }
                }

                return true;
            }
            catch (Exception)
            {
                ////App.API.LogError(ClassName, $"Failed to check logon task: {e}");
                throw; // Throw exception so that App.AutoStartup can show error message
            }
        }

        return false;
    }

    private static bool CheckRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupPath, true);
            if (key != null)
            {
                // Check if the action is the same as the current executable path
                // If not, we need to unschedule and reschedule the task
                var action = (key.GetValue("WinTabber"));
                if (action is string value)
                {
                    if (!value.Equals(GetExecutablePath(), StringComparison.OrdinalIgnoreCase))
                    {
                        DisableRegistry();
                        EnableRegistry();
                    }

                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            //App.API.LogError(ClassName, $"Failed to check registry: {e}");
            throw; // Throw exception so that App.AutoStartup can show error message
        }
    }

    public void DisableViaLogonTaskAndRegistry()
    {
        UnscheduleLogonTask();
        DisableRegistry();
    }

    public void ChangeToViaLogonTask()
    {
        SetRegistry(false);
        SetLogonTask(true);
    }

    public void ChangeToViaRegistry()
    {
        SetLogonTask(false);
        SetRegistry(true);
    }

    private static void SetLogonTask(bool state)
    {
        if (state)
        {
            ScheduleLogonTask();
        }
        else
        {
            UnscheduleLogonTask();
        }
    }

    private static void SetRegistry(bool state)
    {
        if (state)
        {
            EnableRegistry();
        }
        else
        {
            DisableRegistry();
        }
    }

    private static bool ScheduleLogonTask()
    {
        using var td = TaskService.Instance.NewTask();
        td.RegistrationInfo.Description = LogonTaskDesc;
        td.Triggers.Add(new LogonTrigger { UserId = WindowsIdentity.GetCurrent().Name, Delay = TimeSpan.FromSeconds(2) });
        td.Actions.Add(GetExecutablePath());

        if (IsCurrentUserIsAdmin())
        {
            td.Principal.RunLevel = TaskRunLevel.Highest;
        }

        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;

        try
        {
            TaskService.Instance.RootFolder.RegisterTaskDefinition(LogonTaskName, td);
            return true;
        }
        catch (Exception)
        {
            //App.API.LogError(ClassName, $"Failed to schedule logon task: {e}");
            return false;
        }
    }

    private static bool UnscheduleLogonTask()
    {
        using var taskService = new TaskService();
        try
        {
            taskService.RootFolder.DeleteTask(LogonTaskName);
            return true;
        }
        catch (Exception)
        {
            //App.API.LogError(ClassName, $"Failed to unschedule logon task: {e}");
            return false;
        }
    }

    private static bool IsCurrentUserIsAdmin()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool DisableRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupPath, true);
        key?.DeleteValue("WinTabber", false);
        return true;
    }

    private static bool EnableRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupPath, true);
        key?.SetValue("WinTabber", $"\"{GetExecutablePath()}\"");
        return true;
    }
}
