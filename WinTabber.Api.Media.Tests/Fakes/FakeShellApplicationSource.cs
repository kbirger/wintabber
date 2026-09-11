using Microsoft.WindowsAPICodePack.Shell;
using Windows.Win32.UI.Shell;
using WinTabber.Api.Media.ShellApplications;

namespace WinTabber.Api.Media.Tests.Fakes;

public sealed class FakeShellApplicationSource(Func<IKnownFolder> getAppsFolder) : IShellApplicationSource
{
    public IKnownFolder GetAppsFolder() => getAppsFolder();

    public IShellItemImageFactory CreateShellItemImageFactory(string path) =>
        throw new NotSupportedException(
            "Not exercised by these tests — ShellObject/IShellItemImageFactory have no accessible "
                + "test constructor; see the Global Constraints note in this plan."
        );
}
