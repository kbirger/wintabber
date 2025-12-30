using CoreAudio.Interfaces;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WinTabberUI.Infrastructure;

static class AumidHelpers
{
    public static int? TryGetPidForAumid(string aumid)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                unsafe
                {
                    char[] aumidBuffer = new char[512];

                    fixed (char* ptr = aumidBuffer)
                    {
                        uint len = 0;

                        var hr = PInvoke.GetApplicationUserModelId(
                            new HANDLE(process.Handle),
                            &len,
                            new PWSTR());


                        var processAumid = new string(aumidBuffer);

                        if (hr == WIN32_ERROR.NO_ERROR && processAumid.Equals(aumid, StringComparison.OrdinalIgnoreCase))
                            return process.Id;
                    }
                }
            }
            catch
            {
                // Access denied or process exited
            }
        }

        return null;
    }

    public static string? TryGetAumidForPid(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            string? value = TryGetAumid(process);
            return value;
        }
        catch
        {
            // Access denied or process exited
        }

        return null;
    }

    public static string? TryGetAumid(this IAudioSessionControl2 session)
    {
        if (session.GetProcessId(out uint pid) == 0)
        {
            return TryGetAumidForPid(Convert.ToInt32(pid));
        }

        return null;
    }
    public unsafe static string? TryGetAumid(this Process process)
    {
        try
        {
            char[] aumidBuffer = new char[512];
            fixed (char* ptr = aumidBuffer)
            {
                uint len = (uint)aumidBuffer.Length;
                var hr = PInvoke.GetApplicationUserModelId(
                    new HANDLE(process.Handle),
                    &len,
                    new PWSTR(ptr));
                var processAumid = new string(aumidBuffer);

                if (hr == WIN32_ERROR.NO_ERROR)
                {
                    return processAumid;
                }

            }
        }
        catch
        {

        }
        return null;
    }
}