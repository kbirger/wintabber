using ReactiveUI;

namespace WinTabber.ViewModels;

public class WindowRenameViewModel : ReactiveObject
{
    private WindowItem? _windowItem;
    private string _newTitle = "";

    public WindowItem? WindowItem
    {
        get => _windowItem;
        set => this.RaiseAndSetIfChanged(ref _windowItem, value);
    }

    public string NewTitle
    {
        get => _newTitle;
        set => this.RaiseAndSetIfChanged(ref _newTitle, value);
    }

    public void Apply()
    {
        if (WindowItem is not null)
        {
            WindowItem.Title = NewTitle;
        }
    }
}
