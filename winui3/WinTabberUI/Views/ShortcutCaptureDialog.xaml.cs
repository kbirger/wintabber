using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;

namespace WinTabberUI.Views;

public enum ShortcutCaptureDialogResult
{
    Cancelled,
    Saved,
    Deleted,
    ResetToDefault,
}

public sealed partial class ShortcutCaptureDialog : ContentDialog
{
    public ShortcutCaptureDialog(
        string title,
        ShortcutTrigger? initialTrigger,
        IShortcutTriggerSource triggerSource,
        bool canDelete
    )
    {
        InitializeComponent();

        TitleText.Text = title;
        ResultTrigger = initialTrigger;
        DeleteButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = initialTrigger is not null;

        CaptureBox.TriggerSource = triggerSource;
        CaptureBox.Trigger = initialTrigger;
        CaptureBox.Captured += (_, trigger) =>
        {
            ResultTrigger = trigger;
            SaveButton.IsEnabled = true;
            TriggerCaptured?.Invoke(this, trigger);
        };

        // ShortcutCaptureBox normally starts capturing off its own Visibility toggle (see
        // ShortcutCaptureBox.cs's RegisterPropertyChangedCallback on VisibilityProperty) — inside a
        // ContentDialog that never fires reliably, since the control can already report Visible
        // before the dialog itself actually opens. Start explicitly once the dialog has opened, and
        // stop once it closes regardless of how it closed, same as the WPF original's Opened/Closed
        // wiring, just under WinUI 3's own ContentDialog event names.
        Opened += (_, _) => CaptureBox.StartCapture();
        Closed += (_, _) => CaptureBox.CancelCapture();
    }

    public event EventHandler<ShortcutTrigger>? TriggerCaptured;

    public ShortcutCaptureDialogResult Result { get; private set; } = ShortcutCaptureDialogResult.Cancelled;

    public ShortcutTrigger? ResultTrigger { get; private set; }

    public void ShowConflict(string? message)
    {
        ConflictBanner.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        ConflictText.Text = message;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ResultTrigger is null)
        {
            return;
        }

        Result = ShortcutCaptureDialogResult.Saved;
        Hide();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        Result = ShortcutCaptureDialogResult.ResetToDefault;
        Hide();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Result = ShortcutCaptureDialogResult.Deleted;
        Hide();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = ShortcutCaptureDialogResult.Cancelled;
        CaptureBox.CancelCapture();
        Hide();
    }
}
