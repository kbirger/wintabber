using System.Windows.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace WinTabber.UI.Common.Commands;

public class RestoreMaximizeCommand : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    // See MinimizeCommand's note: window state is a method call on AppWindow.Presenter's
    // OverlappedPresenter, not a settable enum property. There is no "Normal" member — only
    // Maximized/Minimized/Restored — so a maximized window is restored, otherwise maximized.
    public void Execute(object? parameter)
    {
        if (parameter is Window window && window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Maximized)
            {
                presenter.Restore();
            }
            else
            {
                presenter.Maximize();
            }
        }
    }
}
