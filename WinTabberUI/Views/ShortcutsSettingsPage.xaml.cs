using ReactiveUI;
using System.Reactive.Linq;
using System.Windows;
using WinTabberUI.ViewModels.Settings;

namespace WinTabberUI.Views
{
    /// <summary>
    /// Interaction logic for ShortcutsSettingsPage.xaml
    /// </summary>
    public partial class ShortcutsSettingsPage
        : ReactivePage<ShortcutsSettingsViewModel>,
            IViewFor<ShortcutsSettingsViewModel>
    {
        public ShortcutsSettingsPage()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ViewModel = e.NewValue as ShortcutsSettingsViewModel;
        }

        private async void OnEditShortcutClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ShortcutBindingViewModel binding } || ViewModel is null)
            {
                return;
            }

            var dialog = new ShortcutCaptureDialog(
                binding.CommandDisplayName,
                binding.Trigger,
                ViewModel.TriggerSource,
                canDelete: true
            );
            dialog.ShowConflict(binding.ConflictMessage);
            dialog.TriggerCaptured += (_, trigger) =>
                dialog.ShowConflict(ViewModel.DescribeConflict(binding.Command, trigger, binding));

            await dialog.ShowAsync();

            switch (dialog.Result)
            {
                case ShortcutCaptureDialogResult.Saved when dialog.ResultTrigger is { } trigger:
                    binding.Trigger = trigger;
                    break;
                case ShortcutCaptureDialogResult.Deleted:
                    binding.RemoveCommand.Execute().Subscribe();
                    break;
                case ShortcutCaptureDialogResult.ResetToDefault:
                    binding.ResetOwnerToDefault();
                    break;
            }
        }

        private async void OnAddShortcutClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ShortcutCommandViewModel command } || ViewModel is null)
            {
                return;
            }

            var dialog = new ShortcutCaptureDialog(command.DisplayName, null, ViewModel.TriggerSource, canDelete: false);
            dialog.TriggerCaptured += (_, trigger) =>
                dialog.ShowConflict(ViewModel.DescribeConflict(command.Command, trigger, excluding: null));

            await dialog.ShowAsync();

            if (dialog.Result == ShortcutCaptureDialogResult.Saved && dialog.ResultTrigger is { } trigger)
            {
                command.AddFromDialog(trigger);
            }
        }
    }
}
