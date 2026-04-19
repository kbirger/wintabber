using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ReactiveUI;
using WinTabber.API;
using WinTabber.Interop;

namespace WinTabberUI.ViewModels;

public class DockWindowViewModel : ReactiveObject
{
    private WindowManager _windowManager;
    private ApplicationRef? _application;
    private string? _applicationName;

    public DockWindowViewModel(WindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public string? ApplicationName
    {
        get => _applicationName;
        set
        {
            this.RaiseAndSetIfChanged(ref _applicationName, value);
            if (value is not null)
                UpdateApplication(value);
        }
    }

    public ObservableCollection<WindowItem> Windows { get; } = new();

    private void UpdateApplication(string newApplicationName)
    {
        _application = new ApplicationRef(newApplicationName, _windowManager);
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        if (_application is null)
            return;
        Windows.Clear();
        foreach (var window in _application.GetWindows())
            Windows.Add(new WindowItem(window, Observable.Empty<bool>()));
    }

    public WindowRef[] GetMaximizedWindows()
    {
        return _application
            ?.GetWindows()
            .Where(w => w.State == WindowPlacement.WindowState.Maximized)
            .ToArray() ?? [];
    }
}
