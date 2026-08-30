using System.Windows;
using iNKORE.UI.WPF.Modern.Controls;
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

/// <summary>
/// Modal capture flow modeled on Flow.Launcher's HotkeyControlDialog: press the keys, see them as
/// large tiles, then explicitly Save. Capture itself is <see cref="WinTabber.UI.Common.Controls.ShortcutCaptureBox" />
/// unchanged — this dialog only changes how it is presented and gates persistence behind Save.
/// </summary>
public partial class ShortcutCaptureDialog : ContentDialog
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

        // ShortcutCaptureBox normally starts capturing off its own IsVisibleChanged (true when a
        // row swaps it in). Inside a ContentDialog that never fires reliably — the content can
        // already report IsVisible before the popup actually opens — so start explicitly once the
        // dialog itself has opened, and stop once it closes regardless of how it closed.
        Opened += (_, _) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shortcut-capture-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} Dialog Opened\n"
            );
            CaptureBox.StartCapture();
        };
        Closed += (_, _) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shortcut-capture-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} Dialog Closed\n"
            );
            CaptureBox.CancelCapture();
        };
    }

    /// <summary>Raised each time capture completes, so the caller can re-check for conflicts.</summary>
    public event EventHandler<ShortcutTrigger>? TriggerCaptured;

    public ShortcutCaptureDialogResult Result { get; private set; } = ShortcutCaptureDialogResult.Cancelled;

    public ShortcutTrigger? ResultTrigger { get; private set; }

    /// <summary>Non-blocking notice, same wording the row itself already shows for a conflict.</summary>
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
