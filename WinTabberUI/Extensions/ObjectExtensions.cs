using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.Extensions;

public static class ObjectExtensions
{
    public static bool In<T>(this T obj, params object[] collection)
    {
        return collection.Contains(obj);
    }
}
