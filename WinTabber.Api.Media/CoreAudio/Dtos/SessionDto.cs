namespace WinTabber.Api.Media.CoreAudio.Dtos;

public class SessionDto : IEquatable<SessionDto>
{
    public required string SessionId { get; init; }
    public required uint ProcessId { get; init; }
    public required string DisplayName { get; init; }

    public bool Equals(SessionDto? other)
    {
        return other is not null
            && SessionId == other.SessionId;
    }

    public override int GetHashCode()
    {
        return SessionId.GetHashCode();
    }
    public override bool Equals(object? other)
    {
        return other is SessionDto dto && Equals(dto);
    }
}
