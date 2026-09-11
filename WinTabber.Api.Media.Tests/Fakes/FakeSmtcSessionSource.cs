using Windows.Media.Control;
using WinTabber.Api.Media.SMTC;

namespace WinTabber.Api.Media.Tests.Fakes;

public sealed class FakeSmtcSessionSource(
    Func<Task<GlobalSystemMediaTransportControlsSessionManager>> requestAsync
) : ISmtcSessionSource
{
    public Task<GlobalSystemMediaTransportControlsSessionManager> RequestAsync() => requestAsync();
}
