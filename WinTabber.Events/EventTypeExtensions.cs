namespace WinTabber.Events;

public static class EventTypeExtensions
{
    public static bool IsOneOf(this EventType type, params EventType[] types)
    {
        return types.Contains(type);
    }
}