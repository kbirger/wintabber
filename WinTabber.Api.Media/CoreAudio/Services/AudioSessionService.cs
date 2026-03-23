using System.Reactive.Linq;
using DynamicData;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Repositories;

namespace WinTabber.Api.Media.CoreAudio.Services;

public partial class AudioSessionService(
    CoreAudioDeviceRepository deviceRepository,
    CoreAudioSessionRepository sessionRepository
)
{
    private readonly CoreAudioSessionRepository _sessionRepository = sessionRepository;
    private readonly CoreAudioDeviceRepository _deviceRepository = deviceRepository;

    [Lazy]
    private IObservable<IChangeSet<SessionDto, string>> GetSessions()
    {
        return _deviceRepository
            .Devices.MergeManyChangeSets(_sessionRepository.Connect)
            .DisposeMany()
            .Transform(session => new SessionDto
            {
                SessionId = session.NativeSession.GetSessionIdentifier,
                ProcessId = session.NativeSession.GetProcessID,
                DisplayName = session.NativeSession.DisplayName,
            })
            .Publish()
            .RefCount();
    }

    public ObservableSessionDto WatchSession(string sessionId)
    {
        var value = _deviceRepository
            .Devices
                .MergeManyChangeSets(_sessionRepository.Connect)
                .AsObservableCache().Lookup(sessionId);
        if(value.HasValue)
        {
            return new ObservableSessionDto(value.Value);
        }

        throw new InvalidOperationException("No such session");
    }
}
