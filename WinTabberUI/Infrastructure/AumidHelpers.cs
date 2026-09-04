using Microsoft.WindowsAPICodePack.Shell;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Threading;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;

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

    public unsafe static string? GetProcessAumid(int processId)
    {
        // Open the process with the necessary access rights
        SafeHandle processHandle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            (uint)processId
        );

        if (processHandle.IsInvalid)
        {
            throw new Exception("Could not open process handle.");
        }

        uint length = 0;
        string? aumid = null;

        WIN32_ERROR result;
        Span<char> s = null;
        result = PInvoke.GetApplicationUserModelId(
            processHandle,
            ref length,
            s);
        // First call to get the buffer size

        var sb = new StringBuilder((int)length);
        if (result == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            Span<char> buffer = stackalloc char[(int)length];
            // Second call to get the actual AUMID string
            result = PInvoke.GetApplicationUserModelId(
                processHandle,
                ref length,
                buffer
            );

            if (result == (uint)WIN32_ERROR.ERROR_SUCCESS)
            {
                aumid = new string(buffer.Slice(0, (int)length - 1)); // Exclude null terminator
            }
        }

        processHandle.Dispose();

        if (result == WIN32_ERROR.ERROR_SUCCESS)
        {
            return aumid;
        }

        if (result == WIN32_ERROR.APPMODEL_ERROR_NO_APPLICATION)
        {
            return $"Process {processId} has no application identity.";
        }

        throw new Exception($"Failed to get AUMID for process {processId}. Error code: {result}");
    }

    public static unsafe string? GetAumidFromAppsFolder(string targetExePath)
    {
        // 1. Get the Shell Item for the Apps Folder (shell:AppsFolder)
        // GUID for FOLDERID_AppsFolder: {1e87508d-89c2-4272-827e-334ed09679a0}
        Guid FOLDERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
        var x = KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);
        //x.
        //HRESULT hr = PInvoke.SHGetKnownFolderItem(FOLDERID_AppsFolder, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, typeof(IShellItem).GUID, out var ppv);

        //if (hr.Failed) return null;
        //IShellItem appsFolder = (IShellItem)ppv;

        //// 2. Bind to the folder's IEnumShellItems to loop through contents
        //Guid enumIid = typeof(IEnumShellItems).GUID;
        //appsFolder.BindToHandler(null, PInvoke.BHID_EnumItems, enumIid, out var enumPpv);
        //IEnumShellItems enumerator = (IEnumShellItems)enumPpv;

        try
        {
            //uint fetched;
            IShellItem[] items = new IShellItem[1];

            //while (enumerator.Next(items, &fetched).Succeeded && fetched == 1)
            foreach (var fetched in (IKnownFolder)x)
            {

                //IShellItem item = items[0];
                try
                {
                    var name = fetched.Name;
                    var id = fetched.ParsingName;
                    var path = fetched.Properties.System.Link.TargetParsingPath.Value;
                    var xxx= fetched.Properties.System.AppUserModel.ID.Value;
                    var yyy = fetched.Properties.System.ParsingPath.Value;
                    if (path == targetExePath)
                    {
                        return id;
                    }
                }
                //    // 3. Get Property Store for this specific app item
                //    Guid propIid = typeof(IPropertyStore).GUID;
                //    bool b = fetched is IShellItem;
                //    ((IShellItem)fetched).BindToHandler(null, PInvoke.BHID_PropertyStore, propIid, out var propPpv);
                //    IPropertyStore store = (IPropertyStore)propPpv;

                //    //// 4. Check if the Exe Path matches
                //    store.GetValue(PInvoke.PKEY_Link_TargetParsingPath, out PROPVARIANT pathVar);
                //    string currentPath = GetStringFromPropVariant(pathVar);

                //    if (string.Equals(currentPath, targetExePath, StringComparison.OrdinalIgnoreCase))
                //    {
                //        //    // 5. Success! Retrieve the AUMID
                //        store.GetValue(PInvoke.PKEY_AppUserModel_ID, out PROPVARIANT aumidVar);
                //        return GetStringFromPropVariant(aumidVar);
                //    }
                //}
                finally
                {
                    //Marshal.ReleaseComObject(fetched);
                }
            }
        }
        finally { 
            //Marshal.ReleaseComObject(x); 
        }

        return null;
    }

    private static unsafe string? GetStringFromPropVariant(PROPVARIANT pv)
    {
        if (pv.Anonymous.Anonymous.vt == VARENUM.VT_LPWSTR)
        {
            Span<char> psz = stackalloc char[260];
            if (PInvoke.PropVariantToString(pv, psz).Succeeded)
                return psz.ToString();
        }
        return null;
    }
    public unsafe static string? TryGetAumid(this Process process)
    {
        var processName = process.ProcessName;
        //var aumid = GetAumidFromWindow(process.MainWindowHandle);
        var aumid = GetAumidFromAppsFolder(process?.MainModule?.FileName ?? "");

        return aumid;
        //return GetProcessAumid(process.Id);
    }
}