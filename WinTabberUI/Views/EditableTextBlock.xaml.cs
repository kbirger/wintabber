using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace WinTabberUI.Views;

public sealed partial class EditableTextBlock : EditableTextBlockBase, INotifyPropertyChanged
{
    public EditableTextBlock()
    {
        InitializeComponent();

        // REAL BUG found via live verification, confirmed with a direct hit-test diagnostic
        // (VisualTreeHelper.FindElementsInHostCoordinates at the click point): the click WAS
        // landing correctly on TitleTextBox -- this was never a layout/hit-testing problem. The
        // XAML `PointerPressed="OnStartEditPointerPressed"` attribute below compiles to a plain
        // `+=` subscription, which WinUI 3's routed-event bubbling skips once something already
        // marked the event Handled -- and TextBox's own internal pointer handling (caret/focus
        // placement) does exactly that before the event ever reaches this handler. WPF's original
        // sidestepped this entirely by using PreviewMouseDown (tunneling, always fires first);
        // WinUI 3 has no tunneling pointer event, but AddHandler's handledEventsToo:true is the
        // documented equivalent -- it receives the event regardless of what already marked it
        // Handled further down. Wired here in code, not XAML, since handledEventsToo has no XAML
        // attribute syntax.
        BorderContainer.AddHandler(PointerPressedEvent, new PointerEventHandler(OnStartEditPointerPressed), true);
        TitleTextBox.AddHandler(PointerPressedEvent, new PointerEventHandler(OnStartEditPointerPressed), true);
    }

    // REAL BUG found via live verification: rename was entirely non-functional -- the WPF
    // original's click-to-edit wiring (BorderContainer_MouseDown: check CanEdit, execute
    // StartEditCommand, focus the TextBox) was dropped entirely from this port. Without it,
    // WindowItem.IsEditing never becomes true, so WindowSelectorViewModel's own IsEditing-gated
    // suppression of the modifier-release close trigger (already correct, shared code -- see
    // WindowSelectorViewModel.cs) never gets a chance to engage: nothing ever tells it a rename is
    // in progress. Reported as "focusing a text box does not block the window from closing" --
    // that symptom is accurate, but the missing piece is upstream of the close logic, not in it.
    // See the constructor's AddHandler doc comment for why this is wired with handledEventsToo
    // rather than the more obvious XAML PointerPressed attribute.
    private void OnStartEditPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsUnderButton(e.OriginalSource as DependencyObject))
        {
            // Let the button handle the click (e.g. the suspend button) instead of entering edit mode.
            return;
        }

        if (ViewModel is null || !ViewModel.CanEdit)
        {
            return;
        }

        using (ViewModel.StartEditCommand.Execute().Subscribe())
        {
        }

        TitleTextBox.Focus(FocusState.Programmatic);
        TitleTextBox.SelectionStart = TitleTextBox.Text.Length;
    }

    /// <summary>Walks up the visual tree from <paramref name="node"/> looking for a <see cref="ButtonBase"/>.</summary>
    private static bool IsUnderButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ButtonBase)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
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
