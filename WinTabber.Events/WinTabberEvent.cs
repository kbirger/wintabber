using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Events;

public record WinTabberEvent(EventType Type)
{
    public static readonly WinTabberEvent None = new WinTabberEvent(0);
    public static implicit operator WinTabberEvent(EventType type) => new WinTabberEvent(type);
}
