using DynamicData;
using WinTabber.UI.Media.Models;

namespace WinTabber.UI.Media.Services;

/// <summary>
/// The seam over <see cref="MediaSessionService"/>.
/// </summary>
/// <remarks>
/// Both members are produced by the <c>[Lazy]</c> source generator from the private
/// <c>GetMasterSessions()</c> / <c>GetActiveSession()</c> methods — the class declares no public
/// members of its own. They are nonetheless the entire surface its consumers use.
/// </remarks>
public interface IMediaSessionService
{
    IObservableCache<AggregateSession, string> MasterSessions { get; }

    IObservable<AggregateSession> ActiveSession { get; }
}
