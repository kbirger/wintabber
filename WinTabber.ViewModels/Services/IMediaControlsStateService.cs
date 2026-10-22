namespace WinTabber.UI.Media.Services;

public interface IMediaControlsStateService
{
    IObservable<bool> IsMediaControlsVisibleChanges { get; }

    void HideView();

    /// <summary>
    /// Tells the service which window is the media controls window. A foreground change to any
    /// other window hides the view.
    /// </summary>
    void RegisterWindow(int handle);
}