using System.Runtime.InteropServices;
using Microsoft.WindowsAPICodePack.Shell;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace WinTabber.Api.Media.ShellApplications;

public sealed class WindowsShellApplicationSource : IShellApplicationSource
{
    private static readonly Guid FOLDERID_AppsFolder = new Guid(
        "{1e87508d-89c2-42f0-8a7e-645a0f50ca58}"
    );
    private static readonly Guid GUID_IShellItem = typeof(IShellItem).GUID;

    public IKnownFolder GetAppsFolder() => KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);

    public unsafe IShellItemImageFactory CreateShellItemImageFactory(string path)
    {
        ShellPInvoke
            .SHCreateItemFromParsingName(path, null, GUID_IShellItem, out var nativeShellItem)
            .ThrowOnFailure();

        if (nativeShellItem is not IShellItemImageFactory imageFactory)
        {
            Marshal.ReleaseComObject(nativeShellItem);
            throw new InvalidOperationException("Failed to get IShellItemImageFactory");
        }

        return imageFactory;
    }
}
