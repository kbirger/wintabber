using WinTabber.Api.Windowing;

namespace WinTabberUI.Services;

public interface IActiveWindowStateService
{
    IObservable<ApplicationRef?> ApplicationChanges { get; }
    IObservable<WindowRef?> WindowChanges { get; }

}