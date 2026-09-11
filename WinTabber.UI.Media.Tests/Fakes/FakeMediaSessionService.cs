using System.Reactive.Linq;
using DynamicData;
using WinTabber.UI.Media.Models;
using WinTabber.UI.Media.Services;

namespace WinTabber.UI.Media.Tests.Fakes;

/// <summary>
/// Always empty: <see cref="ViewModels.MediaControlsViewModel"/>'s activation-timing tests only
/// need <see cref="MasterSessions"/> to be connectable, never to hold a real
/// <c>AggregateSession</c> (which wraps a WinRT session type this test project cannot construct).
/// </summary>
public sealed class FakeMediaSessionService : IMediaSessionService
{
    private readonly SourceCache<AggregateSession, string> _sessions = new(s => s.Key.ToString()!);

    public IObservableCache<AggregateSession, string> MasterSessions => _sessions.AsObservableCache();

    public IObservable<AggregateSession> ActiveSession => Observable.Never<AggregateSession>();
}
