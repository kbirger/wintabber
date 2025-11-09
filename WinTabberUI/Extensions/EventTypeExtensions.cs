using WinTabber.Events;

namespace WinTabberUI.Extensions;

public static class EventTypeExtensions
{
    public static bool IsOneOf(this EventType type, params EventType[] types)
    {
        return types.Contains(type);
    }
}