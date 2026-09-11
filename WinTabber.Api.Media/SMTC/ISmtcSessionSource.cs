using Windows.Media.Control;

namespace WinTabber.Api.Media.SMTC;

/// <summary>
/// The seam over <see cref="GlobalSystemMediaTransportControlsSessionManager.RequestAsync"/> — a
/// static WinRT factory that hits the real SMTC subsystem and cannot be substituted in unit tests
/// without this interface.
/// </summary>
public interface ISmtcSessionSource
{
    Task<GlobalSystemMediaTransportControlsSessionManager> RequestAsync();
}
