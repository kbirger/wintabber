using Windows.Media.Control;

namespace WinTabber.Api.Media.SMTC;

public sealed class SmtcSessionSource : ISmtcSessionSource
{
    public async Task<GlobalSystemMediaTransportControlsSessionManager> RequestAsync() =>
        await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
}
