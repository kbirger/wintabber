using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinTabber.ViewModels;
using WinTabber.ViewModels.Settings;
using WinUIEx;

namespace WinTabberUI.Views;

public sealed partial class SettingsWindow : WindowEx
{
    private bool _syncingSelectionFromViewModel;

    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // TODO(verify) resolution: `SystemBackdrop="{winuiex:MicaBackdrop}"` in XAML compiles in
        // pass1 but crashes this SDK's XamlCompiler pass2 with no diagnostic (confirmed against real
        // compiler output: MSB3073, XamlCompiler.exe exits 1 with zero stdout/stderr; removing only
        // this one attribute was sufficient to make an otherwise-identical file build clean). Using
        // the brief's specified code-behind fallback instead, which is guaranteed to work since
        // SystemBackdrop is a plain settable property regardless of what WindowEx exposes in XAML.
        SystemBackdrop = new MicaBackdrop();

        // WPF's original bound NavigationView's SelectedItem two-way against ViewModel.SelectedView.
        // WinUI 3's NavigationView.SelectedItem has no x:Bind Mode=TwoWay support, so we replicate
        // the ViewModel->View direction by hand: seed the initial selection (SelectedView is already
        // `General` by the time the constructor runs) and keep it in sync with any later change
        // (e.g. a future feature that opens Settings to a specific section programmatically).
        SettingsNavigationView.SelectedItem = ViewModel.SelectedView;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SettingsViewModel.SelectedView))
        {
            return;
        }

        if (ReferenceEquals(SettingsNavigationView.SelectedItem, ViewModel.SelectedView))
        {
            return;
        }

        _syncingSelectionFromViewModel = true;
        try
        {
            SettingsNavigationView.SelectedItem = ViewModel.SelectedView;
        }
        finally
        {
            _syncingSelectionFromViewModel = false;
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelectionFromViewModel)
        {
            return;
        }

        if (args.SelectedItem is SettingsViewModelBase section)
        {
            ViewModel.SelectedView = section;
        }
    }
}
