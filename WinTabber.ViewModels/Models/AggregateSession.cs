using System.Diagnostics.CodeAnalysis;
using Windows.Media.Control;
using WinTabber.Api.Media.CoreAudio.Models;
using WinTabber.Api.Media.ShellApplications.Models;

namespace WinTabber.UI.Media.Models;

public class AggregateSession(
        GlobalSystemMediaTransportControlsSession MediaSession,
        InstalledApplicationInfo App,
        CoreAudioSessionWrapper? NativeSession
    ) : IEquatable<AggregateSession>
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

    public object Key => (IsComplete, MediaSession.SourceAppUserModelId);

    public override bool Equals(object? obj)
    {
        return Equals (obj as AggregateSession);
    }

    public bool Equals(AggregateSession? other)
    {
        return Key.Equals(other?.Key);
    }

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }
}
