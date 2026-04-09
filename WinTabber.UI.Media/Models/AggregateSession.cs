using System.Diagnostics.CodeAnalysis;
using Windows.Media.Control;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.ShellApplications.Models;

namespace WinTabber.UI.Media.Models;

public sealed class AggregateSession(
        GlobalSystemMediaTransportControlsSession MediaSession,
        InstalledApplicationInfo App,
        CoreAudioSessionWrapper? NativeSession
    )
{
    public GlobalSystemMediaTransportControlsSession MediaSession { get; } = MediaSession;
    public InstalledApplicationInfo App { get; } = App;
    public CoreAudioSessionWrapper? NativeSession { get; private set; } = NativeSession;

    public void UpdateNativeSession(CoreAudioSessionWrapper? newNativeSession)
    {
        NativeSession = newNativeSession;
    }

    [MemberNotNullWhen(true, nameof(NativeSession))]
    public bool IsComplete => NativeSession != null;
}
