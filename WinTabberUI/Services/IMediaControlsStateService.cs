
namespace WinTabberUI.Services;

public interface IMediaControlsStateService
{
    IObservable<bool> IsMediaControlsVisibleChanges { get; }
}