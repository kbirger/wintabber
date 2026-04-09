namespace WinTabber.Events;

public record WinTabberEvent(EventType Type)
{
    public static readonly WinTabberEvent None = new WinTabberEvent(0);
    public static implicit operator WinTabberEvent(EventType type) => new WinTabberEvent(type);
}
