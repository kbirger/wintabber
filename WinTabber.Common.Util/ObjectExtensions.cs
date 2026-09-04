namespace WinTabber.Common.Util;

public static class ObjectExtensions
{
    public static bool In<T>(this T obj, params object[] collection)
    {
        return collection.Contains(obj!);
    }
}
