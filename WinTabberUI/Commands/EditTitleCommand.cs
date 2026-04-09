using System.Windows.Input;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Commands;

internal class EditTitleCommand : ICommand
{
    private WindowItem _windowItem;

    public EditTitleCommand(WindowItem windowItem)
    {
        _windowItem = windowItem;
    }
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        if(parameter is string newTitle)
        {
            _windowItem.Title = newTitle;
        }
    }
}
