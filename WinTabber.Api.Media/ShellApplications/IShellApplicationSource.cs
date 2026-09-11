using Microsoft.WindowsAPICodePack.Shell;
using Windows.Win32.UI.Shell;

namespace WinTabber.Api.Media.ShellApplications;

/// <summary>
/// The seam over the static Windows Shell APIs <see cref="InstalledApplicationRepository"/> used
/// to call directly — <c>KnownFolderHelper.FromKnownFolderId</c> and
/// <c>PInvoke.SHCreateItemFromParsingName</c> — both of which hit the real Windows shell and
/// cannot be substituted in unit tests without this interface.
/// </summary>
public interface IShellApplicationSource
{
    IKnownFolder GetAppsFolder();
    IShellItemImageFactory CreateShellItemImageFactory(string path);
}
