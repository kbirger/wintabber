namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class SessionDto
{
    public required string SessionId { get; init; }
    public required uint ProcessId { get; init; }
    public required string DisplayName { get; init; }
}
