using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;

public static class ProcessHelper
{
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

    public static IEnumerable<ProcessInfo> GetNonSystemProcesses()
    {
        Dictionary<int, bool> processMap = new();
        foreach (var process in GetProcesses())
        {
            var isSelfSystem = process.Id == 0 || process.ParentId == 0 || string.Equals(process.ProcessName, "svchost", StringComparison.OrdinalIgnoreCase);
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
            }
            while (PInvoke.Process32Next(h, ref entry));
        }
    }

    private static unsafe ProcessInfo GetProcessInfo(uint th32ProcessID, Windows.Win32.Foundation.__CHAR_260 szExeFile, uint th32ParentProcessID)
    {
        byte* processNameByte = (byte*)&szExeFile;
        int pid = (int)th32ProcessID;
        int parent = (int)th32ParentProcessID;
        string processName = Path.GetFileNameWithoutExtension(
            Encoding.UTF8.GetString(
                processNameByte,
                GetLength(processNameByte)
            )
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