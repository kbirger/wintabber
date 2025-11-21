using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Events;

public enum EventType
{
    CmdNextWindow,
    CmdPreviousWindow,
    CmdAppHide,
    CmdMinimizeWindow,
    CmdMaximizeWindow,
    ActiveWindowChanged,
    ActiveApplicatonChanged,
    CmdMediaWindow,
    CmdDockWindow,
    CmdShowSettings,
    WindowSelected,
    EditingStateChanged,
}
