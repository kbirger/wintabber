using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Subjects;
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

    private readonly Subject<Unit> _nativeSessionChanged = new();

    // Confirmed live: a device switch (e.g. the default playback device changing) produces a new
    // CoreAudioSessionWrapper for the same app/AUMID, matched here via UpdateNativeSession. Key
    // (below) deliberately stays (IsComplete, AppUserModelId) -- unrelated to the device -- so a
    // consumer gating on Key/Equals (MediaSessionViewModel.Session's RaiseAndSetIfChanged,
    // MediaControlsViewModel's DistinctUntilChanged(session => session.Key)) never sees this
    // mutation as a change, since it is the same object reference throughout. This observable is
    // the only way a consumer can learn the device actually changed underneath it.
    public IObservable<Unit> NativeSessionChanged => _nativeSessionChanged;

    public void UpdateNativeSession(CoreAudioSessionWrapper? newNativeSession)
    {
        NativeSession = newNativeSession;
        _nativeSessionChanged.OnNext(Unit.Default);
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
