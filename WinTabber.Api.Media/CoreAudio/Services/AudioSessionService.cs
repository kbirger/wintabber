using System.Reactive.Linq;
using DynamicData;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Repositories;

namespace WinTabber.Api.Media.CoreAudio.Services;

public class AudioSessionService(
    CoreAudioDeviceRepository deviceRepository,
    CoreAudioSessionRepository sessionRepository
)
{
    private readonly CoreAudioSessionRepository _sessionRepository = sessionRepository;
    private readonly CoreAudioDeviceRepository _deviceRepository = deviceRepository;

    public IObservable<IChangeSet<SessionDto, string>> GetSessions()
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
}
