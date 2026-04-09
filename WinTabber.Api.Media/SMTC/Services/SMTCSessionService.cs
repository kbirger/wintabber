using DynamicData;
using System.Reactive.Linq;
using WinTabber.Api.Media.SMTC.Dtos;
using WinTabber.Api.Media.SMTC.Repositories;

namespace WinTabber.Api.Media.SMTC.Services;

public class SMTCSessionService(SMTCSessionRepository sessionRepository)
{
    private readonly SMTCSessionRepository _sessionRepository = sessionRepository;

    public IObservable<IChangeSet<SMTCSessionDto, string>> GetSessions()
    {
        return _sessionRepository.MediaSessions
            .Transform(session => new SMTCSessionDto { Aumid = session.SourceAppUserModelId })
            .Publish()
            .RefCount();
    }

    public IObservable<SMTCSessionDto> GetActiveSession()
    {
        return _sessionRepository.ActiveMediaSessionChanges
            .Select(session => new SMTCSessionDto { Aumid = session.SourceAppUserModelId })
            .Publish()
            .RefCount();
    }
}
