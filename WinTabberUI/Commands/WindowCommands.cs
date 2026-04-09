using System.Windows.Input;

namespace WinTabberUI.Commands
{
    public static class WindowCommands
    {
        public static ICommand Maximize { get; } = new RestoreMaximizeCommand();
        public static ICommand Minimize { get;  } = new MinimizeCommand();
    }
}
