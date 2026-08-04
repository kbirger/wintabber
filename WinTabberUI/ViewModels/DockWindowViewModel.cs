using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ReactiveUI;
using WinTabber.API;
using WinTabber.API.Suspension;
using WinTabber.API.Thumbnails;
using WinTabber.Interop;

namespace WinTabberUI.ViewModels;

public class DockWindowViewModel : ReactiveObject
{
    private WindowManager _windowManager;
    private readonly IProcessSuspensionService _suspensionService;
    private readonly IWindowThumbnailService _thumbnailService;
    private ApplicationRef? _application;
    private string? _applicationName;

    public DockWindowViewModel(WindowManager windowManager, IProcessSuspensionService suspensionService, IWindowThumbnailService thumbnailService)
    {
        _windowManager = windowManager;
        _suspensionService = suspensionService;
        _thumbnailService = thumbnailService;
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
            Windows.Add(new WindowItem(window, Observable.Empty<bool>(), _suspensionService, _thumbnailService));
    }

    public WindowRef[] GetMaximizedWindows()
    {
        return _application
            ?.GetWindows()
            .Where(w => w.State == WindowPlacement.WindowState.Maximized)
            .ToArray() ?? [];
    }
}
