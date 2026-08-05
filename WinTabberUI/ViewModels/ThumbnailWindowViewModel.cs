using ReactiveUI;

namespace WinTabberUI.ViewModels;

/// <summary>
/// Backs a single floating <see cref="ThumbnailWindow"/> instance. Registered transient in DI; the window
/// factory populates <see cref="Handle"/>/<see cref="Title"/> right after construction via <see cref="Initialize"/>,
/// mirroring how <c>WindowSelectorWindowFactory</c> wires up its window's DataContext post-construction.
///
/// Purely a display-data holder — restoring the source window is handled by <see cref="ThumbnailWindow"/>'s
/// <c>OnClosing</c>, since it needs the window's own current size (for the zoom factor) to do so, which
/// isn't something a ViewModel should reach into the View for.
/// </summary>
public class ThumbnailWindowViewModel : ReactiveObject
{
    private int _handle;
    public int Handle
    {
        get => _handle;
        private set => this.RaiseAndSetIfChanged(ref _handle, value);
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        private set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public void Initialize(int handle, string title)
    {
        Handle = handle;
        Title = title;
    }
}
