using System.Reactive.Linq;
using WinTabberUI.Services;

namespace WinTabberUI.ViewModels;
public class ApplicationStateViewModelFactory(IMediaControlsStateService mediaControlsStateService, IActiveWindowStateService activeWindowStateService)
{
    //private readonly IWindowSelectorStateService _windowSelectorStateService = windowSelectorStateService;
    private readonly IMediaControlsStateService _mediaControlsStateService = mediaControlsStateService;
    private readonly IActiveWindowStateService _activeWindowStateService = activeWindowStateService;


    public ApplicationStateViewModel CreateApplicationStateViewModel()
    {
        return new ApplicationStateViewModel
        {
            ActiveApplicationChanges = _activeWindowStateService.ApplicationChanges,
            ActiveWindowChanges = _activeWindowStateService.WindowChanges,
            IsDockActiveChanges = Observable.Empty<bool>(),
            IsMediaControlsActiveChanges = _mediaControlsStateService.IsMediaControlsVisibleChanges,
        };
    }
}
