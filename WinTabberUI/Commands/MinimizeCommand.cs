using System.Windows;
using System.Windows.Input;

namespace WinTabberUI.Commands
{
    public class MinimizeCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            if(parameter is Window window)
            {
                window.WindowState = WindowState.Minimized;
            }
        }
    }
}
