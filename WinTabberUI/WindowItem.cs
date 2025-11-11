using ReactiveUI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinTabber.API;
using WinTabberUI.Commands;

namespace WinTabberUI;

public class WindowItem : INotifyPropertyChanged
{
    public WindowItem(WindowRef windowRef)
    {
        WindowRef = windowRef ?? throw new ArgumentNullException(nameof(windowRef));
        //Icon = WindowRef.GetIcon().ToImageSource(); 
        //EditTitleCommand = new EditTitleCommand(this);
        var editingChanges = this.WhenAnyValue(item => item.IsEditing);
        EditTitleCommand = ReactiveCommand.Create((string value) => Title = value, editingChanges);
        CancelEditTitleCommand = ReactiveCommand.Create(() => IsEditing = false, editingChanges);
    }

    public WindowRef WindowRef { get; }

    public ICommand EditTitleCommand { get; }
    public ReactiveCommand<Unit, bool> CancelEditTitleCommand { get; }

    public string ProcessName => WindowRef.Process.ProcessInstance.ProcessName;

    public string Title
    {
        get
        {
            return WindowRef.Title;
        }
        set
        {
            WindowRef.SetTitle(value);
            OnPropertyChanged(nameof(Title));
        }
    }

    public IntPtr Handle => WindowRef.Handle;

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool _isEditing = false;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if(_isEditing != value)
            {
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
            }
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    //public ImageSource Icon { get; set; }

    public void Activate() => WindowRef.BringToFront();


}
