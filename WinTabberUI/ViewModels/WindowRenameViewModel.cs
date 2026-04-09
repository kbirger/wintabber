using System.Windows;

namespace WinTabberUI.ViewModels;

public class WindowRenameViewModel : DependencyObject
{
    private DependencyProperty _windowItem = DependencyProperty.Register("WindowItem", typeof(WindowItem), typeof(WindowRenameViewModel));

    public WindowItem? WindowItem
    {
        get => (WindowItem)GetValue(_windowItem);
        set =>  SetValue(_windowItem, value);
    }

    private DependencyProperty _newName = DependencyProperty.Register("NewTitle", typeof(string), typeof(WindowRenameViewModel));

    public string NewTitle
    {
        get => (string)GetValue(_newName);
        set => SetValue(_newName, value);
    }

    public void Apply()
    {
        if (WindowItem is not null)
        {
            WindowItem.Title = NewTitle;
        }
    }
}