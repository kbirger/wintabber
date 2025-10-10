using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Events
{
    public record WinTabberEvent(EventType type, object? data = null)
    {
        public WinTabberEvent(EventType type) : this(type, null) { }

        public static implicit operator WinTabberEvent(EventType type) => new WinTabberEvent(type);
    }
}
