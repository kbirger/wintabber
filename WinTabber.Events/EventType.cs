using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Events
{
    public enum EventType
    {
        NextWindow,
        PreviousWindow,
        AppHide,
        MinimizeWindow,
        MaximizeWindow,
        ForegroundChanged
    }
}
