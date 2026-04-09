using System.Windows.Input;

namespace WinTabber.UI.Common.Commands
{
    public static class WindowCommands
    {
        public static ICommand Maximize { get; } = new RestoreMaximizeCommand();
        public static ICommand Minimize { get;  } = new MinimizeCommand();
    }
}
