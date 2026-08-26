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

    // Appended only. Ordinal 0 is CmdNextWindow, and WinTabberEvent.None is new WinTabberEvent(0),
    // so inserting a member here would both remap every ordinal and change what "None" means.
    CmdCommitSelection,
    CmdThumbnailWindow,
    CmdSuspendedWindows,
    CmdSuspendWindow,
}
