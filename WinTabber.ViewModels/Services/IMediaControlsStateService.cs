namespace WinTabber.UI.Media.Services;

public interface IMediaControlsStateService
{
    IObservable<bool> IsMediaControlsVisibleChanges { get; }

    void HideView();
}