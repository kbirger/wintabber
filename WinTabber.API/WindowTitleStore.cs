using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.API;

public class WindowTitleStore
{
    private Dictionary<int, string> _cache = new Dictionary<int, string>();
    private object _lock = new object();
    public void Update(IReadOnlyList<int> activeWindows)
    {
        lock (_lock)
        {

            var newCache = activeWindows
                .Where(x => _cache.ContainsKey(x))
                .Select(handle => new KeyValuePair<int, string>(handle, _cache[handle]))
                .ToDictionary();

            _cache = newCache;
        }
    }

    public bool TryGetTitleOverride(int handle, out string titleOverride)
    {
        lock(_lock)
        {
            return _cache.TryGetValue(handle, out titleOverride!);
        }
    }

    public void OverrideTitle(int handle, string title)
    {
        lock( _lock)
        {
            _cache[handle] = title;
        }
    }
}
