using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace WinTabberUI.Views;

public sealed partial class EditableTextBlock : EditableTextBlockBase, INotifyPropertyChanged
{
    public EditableTextBlock()
    {
        InitializeComponent();
    }

    // WPF's IsMouseOver has no WinUI 3 FrameworkElement-level equivalent (WinUI 3 only exposes
    // PointerEntered/PointerExited events) — this is the plain bool the brief's item 1 asks for,
    // toggled by those two events and bound from BorderContainer.Background via x:Bind. x:Bind's
    // OneWay mode only refreshes when the bound-to type raises PropertyChanged for the property
    // (or Bindings.Update() is called explicitly), so this control implements INotifyPropertyChanged
    // itself for this one property rather than relying on ReactiveUserControl's ReactiveObject
    // machinery, which only covers the ViewModel, not this view's own code-behind state.
    private bool _isPointerOver;

    public bool IsPointerOver
    {
        get => _isPointerOver;
        private set
        {
            if (_isPointerOver == value)
            {
                return;
            }

            _isPointerOver = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void OnPointerEnteredRoot(object sender, PointerRoutedEventArgs e) => IsPointerOver = true;

    private void OnPointerExitedRoot(object sender, PointerRoutedEventArgs e) => IsPointerOver = false;

    // x:Bind TwoWay on TextBox.Text only pushes to the source on LostFocus, not per keystroke
    // (unlike WPF's UpdateSourceTrigger=PropertyChanged, which the WPF original relied on) — without
    // this handler, pressing Enter while the box still has focus would save/validate the stale
    // ViewModel.Title instead of what's currently typed. Mirrors the WPF original's
    // UpdateSourceTrigger=PropertyChanged behavior explicitly.
    private void OnTitleTextBoxTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Title = TitleTextBox.Text;
        }
    }

    private void OnTitleTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                var saveCommand = (ICommand)ViewModel.SaveTitleCommand;
                if (saveCommand.CanExecute(ViewModel.Title))
                {
                    saveCommand.Execute(ViewModel.Title);
                }
                return;
            case VirtualKey.Escape:
                e.Handled = true;
                var cancelCommand = (ICommand)ViewModel.CancelEditTitleCommand;
                if (cancelCommand.CanExecute(null))
                {
                    cancelCommand.Execute(null);
                }
                return;
        }
    }
}
