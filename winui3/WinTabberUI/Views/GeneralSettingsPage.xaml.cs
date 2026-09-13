using ReactiveUI;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

public sealed partial class GeneralSettingsPage : GeneralSettingsPageBase, IViewFor<GeneralSettingsViewModel>
{
    public GeneralSettingsPage()
    {
        InitializeComponent();
        this.WhenActivated((dispose) =>
        {
            this.Bind(
                ViewModel,
                vm => vm.StartupMode,
                view => view.StartupList.SelectedValue,
                signalViewUpdate: Observable.FromEventPattern(StartupList, nameof(StartupList.SelectionChanged))
            ).DisposeWith(dispose);

            this.Bind(
                ViewModel,
                vm => vm.ThumbnailResizeMode,
                view => view.ThumbnailResizeModeList.SelectedValue,
                signalViewUpdate: Observable.FromEventPattern(ThumbnailResizeModeList, nameof(ThumbnailResizeModeList.SelectionChanged))
            ).DisposeWith(dispose);
        });
    }
}
