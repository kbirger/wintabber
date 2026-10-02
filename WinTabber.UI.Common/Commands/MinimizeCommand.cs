using System.Windows.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace WinTabber.UI.Common.Commands;

public class MinimizeCommand : ICommand
{
    // WPF's CommandManager.RequerySuggested has no WinUI3 equivalent; these commands' CanExecute
    // is always true and never changes, so the event is a legal no-op add/remove rather than a
    // real subscription. If a future caller needs CanExecute to actually vary, raise this event
    // manually from Execute or a property setter instead of reaching for RequerySuggested.
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    // NOTE: Microsoft.UI.Xaml.Window has no settable WindowState property (confirmed in Task 2.1
    // via compiler error CS0246 on Microsoft.UI.Xaml.WindowState). Window state instead lives on
    // the window's AppWindow.Presenter as an OverlappedPresenter, which exposes Minimize()/
    // Maximize()/Restore() methods rather than a settable enum.
    public void Execute(object? parameter)
    {
        if (parameter is Window window && window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }
}
