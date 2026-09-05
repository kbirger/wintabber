using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace WinTabber.Interop;

internal static class UacHelper
{
    private const string uacRegistryKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";
    private const string uacRegistryValue = "EnableLUA";

    public enum TOKEN_ELEVATION_TYPE
    {
        TokenElevationTypeDefault = 1,
        TokenElevationTypeFull,
        TokenElevationTypeLimited
    }

    public static bool IsUacEnabled
    {
        get
        {
            try
            {

                RegistryKey? uacKey = Registry.LocalMachine.OpenSubKey(uacRegistryKey, false);
                if (uacKey is not null)
                {
                    
                    bool result = uacKey.GetValue(uacRegistryValue)?.Equals(1) ?? true;
                    return result;
                }
            }
            catch
            {
                return true;
            }

            return true;
        }
    }

    public static unsafe bool IsProcessElevated(Process process)
    {
        if (IsUacEnabled)
        {
            try
            {
                if (!PInvoke.OpenProcessToken(process.SafeHandle, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var tokenHandle))
                {
                    throw new ApplicationException("Could not get process token.  Win32 Error Code: " + Marshal.GetLastWin32Error());
                }

                using (tokenHandle)
                {
                    var tokenHandleNative = new HANDLE(tokenHandle.DangerousGetHandle());
                    TOKEN_ELEVATION_TYPE elevationResult = TOKEN_ELEVATION_TYPE.TokenElevationTypeDefault;
                    uint returnedSize = 0;

                    bool success = PInvoke.GetTokenInformation(
                        tokenHandleNative,
                        TOKEN_INFORMATION_CLASS.TokenElevationType,
                        &elevationResult,
                        (uint)sizeof(TOKEN_ELEVATION_TYPE),
                        &returnedSize);

                    if (success)
                    {
                        return elevationResult == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
                    }
                    else
                    {
                        throw new ApplicationException("Unable to determine the current elevation.");
                    }
                }
            }
            catch
            {
                return true;
            }
        }
        else
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            bool result = principal.IsInRole(WindowsBuiltInRole.Administrator);
            return result;
        }
    }
}
