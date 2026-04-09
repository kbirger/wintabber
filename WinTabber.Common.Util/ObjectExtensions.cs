using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Common.Util;

public static class ObjectExtensions
{
    public static bool In<T>(this T obj, params object[] collection)
    {
        return collection.Contains(obj);
    }
}
