using System.Windows;
using System.Windows.Input;

namespace WinTabber.UI.Common.Commands
{
    public class MinimizeCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

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
