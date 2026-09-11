using Microsoft.WindowsAPICodePack.Shell;
using Windows.Win32.UI.Shell;

namespace WinTabber.Api.Media.ShellApplications;

/// <summary>
/// The seam over the static Windows Shell APIs <see cref="Repositories.InstalledApplicationRepository"/>
/// used to call directly — <c>KnownFolderHelper.FromKnownFolderId</c> and
/// <c>ShellPInvoke.SHCreateItemFromParsingName</c> — both of which hit the real Windows shell and
/// cannot be substituted in unit tests without this interface.
/// </summary>
/// <remarks>
/// Deliberately narrower than <c>IMMDeviceEnumeratorWrapper</c>, which returns a project-owned
/// <c>IAudioDevice</c> rather than raw <c>MMDevice</c>. Wrapping <c>IKnownFolder</c>/
/// <c>IShellItemImageFactory</c> behind project-owned model types would also require abstracting
/// <see cref="ShellObject"/>, which has no accessible test constructor — a much bigger, out-of-scope
/// design pass. This interface only makes *acquisition* substitutable, not full shell-item
/// processing.
/// </remarks>
public interface IShellApplicationSource
{
    /// <returns>A disposable the caller owns (see the <c>using var folder = ...</c> call site).</returns>
    IKnownFolder GetAppsFolder();

    /// <returns>
    /// A COM object the caller must release via <see cref="System.Runtime.InteropServices.Marshal.ReleaseComObject(object)"/>
    /// (see the <c>finally { Marshal.ReleaseComObject(imageFactory); }</c> call site).
    /// </returns>
    IShellItemImageFactory CreateShellItemImageFactory(string path);
}
