using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using Windows.Wdk.System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;

public static class ProcessHelper
{
    private static HashSet<string> _knownSystemProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "svchost",
        "idle",
    };

    public static bool IsSystemProcess(Process process)
    {
        return process.Id == 0 || _knownSystemProcessNames.Contains(process.ProcessName);
    }

    public static bool IsSystemProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return IsSystemProcess(process);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSystemProcess(uint processId)
    {
        return IsSystemProcess((int)processId);
    }

    extension(Process process)
    {
        public bool IsSystemProcess => IsSystemProcess(process);
        public Process? Parent => GetParentProcess(Process.GetProcessById(process.Id));

        public IEnumerable<Process> GetAncestors() => GetAncestors(process.Id);
        public bool TryGetExecutablePath([MaybeNullWhen(false)] out string executablePath) =>
            TryGetProcessExecutablePath((uint)process.Id, out executablePath);
    }

    extension(ProcessInfo process)
    {
        public bool IsSystemProcess => IsSystemProcess(process.Id);

        public Process? Parent => GetParentProcess(Process.GetProcessById(process.Id));

        public bool TryGetExecutablePath([MaybeNullWhen(false)] out string executablePath) =>
            TryGetProcessExecutablePath((uint)process.Id, out executablePath);
    }

    public static bool TryGetProcessExecutablePath(uint pid, [MaybeNullWhen(false)] out string executablePath)
    {
        using var hProcess = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            pid
        );

        if (hProcess.IsInvalid)
        {
            executablePath = null;
            return false;
        }

        uint size = 1024;
        Span<char> psz = new char[size].AsSpan();

        if (PInvoke.QueryFullProcessImageName(hProcess, 0, psz, ref size))
        {
            executablePath = psz.Slice(0, (int)size).ToString();
            if (size > 0 && !string.IsNullOrWhiteSpace(executablePath))
            {
                return true;
            }
        }
        executablePath = null;
        return false;
    }

    // [DllImport("kernel32.dll", SetLastError = true)]
    // static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    // [DllImport("kernel32.dll", SetLastError = true)]
    // static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    // [DllImport("kernel32.dll", SetLastError = true)]
    // static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    // [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    // struct PROCESSENTRY32
    // {
    //     public uint dwSize;
    //     public uint cntUsage;
    //     public uint th32ProcessID;
    //     public IntPtr th32DefaultHeapID;
    //     public uint th32ModuleID;
    //     public uint cntThreads;
    //     public uint th32ParentProcessID;
    //     public int pcPriClassBase;
    //     public uint dwFlags;

    //     [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    //     public string szExeFile;
    // }

    // const uint TH32CS_SNAPPROCESS = 0x00000002;

    public static IEnumerable<Process> GetAncestors(uint processId)
    {
        return GetAncestors((int)processId);
    }

    public static IEnumerable<Process> GetAncestors(int processId)
    {
        var process = Process.GetProcessById(processId);
        while (process is not null && process.Id > 0)
        {
            yield return process;
            try
            {
                if (_knownSystemProcessNames.Contains(process.ProcessName))
                {
                    break;
                }

                process = GetParentProcess(process);
            }
            catch
            {
                break;
            }
        }
    }

    public static unsafe Process? GetParentProcess(Process process)
    {
        Stopwatch sw = Stopwatch.StartNew();
        PROCESS_BASIC_INFORMATION pbi;
        uint length = 0;
        var result = Windows.Wdk.PInvoke.NtQueryInformationProcess(
            new HANDLE(process.Handle),
            PROCESSINFOCLASS.ProcessBasicInformation,
            &pbi,
            (uint)Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(),
            ref length
        );

        if (result.SeverityCode > NTSTATUS.Severity.Informational)
        {
            return null;
            //Marshal.ThrowExceptionForHR((int)result);
        }

        sw.Stop();
        //Debug.WriteLine($"GetParentProcess => {pbi.InheritedFromUniqueProcessId} for {process.ProcessName} ({process.Id}) took {sw.ElapsedMilliseconds}ms");
        if (pbi.InheritedFromUniqueProcessId != 0)
        {
            return Process.GetProcessById((int)pbi.InheritedFromUniqueProcessId);
        }
        return null;
    }

    public static IEnumerable<ProcessInfo> GetNonSystemProcesses()
    {
        Dictionary<int, bool> processMap = new();
        foreach (var process in GetProcesses())
        {
            var isSelfSystem =
                process.Id == 0
                || process.ParentId == 0
                || string.Equals(process.ProcessName, "svchost", StringComparison.OrdinalIgnoreCase);
            var isParentSystem = processMap.GetValueOrDefault(process.ParentId, false);
            var isSystem = isSelfSystem || isParentSystem;
            processMap.Add(process.Id, isSystem);
            if (!isSystem)
            {
                yield return process;
            }
        }
    }

    public static IEnumerable<ProcessInfo> GetProcessesByName(string processName)
    {
        foreach (var process in GetProcesses())
        {
            if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            {
                yield return process;
            }
        }
    }

    public static IEnumerable<ProcessInfo> GetProcesses()
    {
        var h = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (h.IsInvalid)
        {
            yield break;
        }

        var entry = new PROCESSENTRY32();
        entry.dwSize = (uint)Marshal.SizeOf(entry);

        if (PInvoke.Process32First(h, ref entry))
        {
            do
            {
                yield return GetProcessInfo(entry.th32ProcessID, entry.szExeFile, entry.th32ParentProcessID);
            } while (PInvoke.Process32Next(h, ref entry));
        }
    }

    private static unsafe ProcessInfo GetProcessInfo(
        uint th32ProcessID,
        Windows.Win32.Foundation.__CHAR_260 szExeFile,
        uint th32ParentProcessID
    )
    {
        byte* processNameByte = (byte*)&szExeFile;
        int pid = (int)th32ProcessID;
        int parent = (int)th32ParentProcessID;
        string processName = Path.GetFileNameWithoutExtension(
            Encoding.UTF8.GetString(processNameByte, GetLength(processNameByte))
        );
        return new(pid, processName, parent);
    }

    static unsafe int GetLength(byte* pszStr)
    {
        int len = 0;
        while (*pszStr++ != 0)
        {
            len += 1;
        }

        return len;
    }
}
