namespace WinTabber.Events;

public record WinTabberEvent<T>(EventType Type, T Arg) : WinTabberEvent(Type)
{
    public static implicit operator WinTabberEvent<T>((EventType type, T arg) values) => new WinTabberEvent<T>(values.type, values.arg);
}
