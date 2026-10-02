using Microsoft.UI.Xaml;
using ReactiveUI;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

// WinUI 3's XAML compiler does not propagate x:TypeArguments from a generic base class onto the
// generated partial class (microsoft/microsoft-ui-xaml#7746, closed as not planned) — the generated
// code-behind ends up as `partial class GeneralSettingsPage : ReactiveUI.ReactivePage` with no type
// argument, which fails to compile (CS0305). The standard workaround is this non-generic
// intermediate base class: GeneralSettingsPage.xaml roots on GeneralSettingsPageBase instead of
// rxwpf:ReactivePage directly, so the XAML compiler only ever sees a closed (non-generic) base type.
//
// Neither ReactiveUI.Wpf's nor ReactiveUI.WinUI's ReactivePage<TViewModel> wires ViewModel from
// DataContext automatically (confirmed by decompiling both — ViewModel is a bare
// DependencyProperty in each, with no constructor logic or DataContextChanged subscription). The
// WPF original (WinTabberUI/Views/GeneralSettingsPage.xaml.cs) did this itself via an explicit
// DataContextChanged handler; that wiring belongs here, once, rather than in the derived partial
// class, since it applies regardless of what the derived class adds on top.
public class GeneralSettingsPageBase : ReactivePage<GeneralSettingsViewModel>
{
    public GeneralSettingsPageBase()
    {
        DataContextChanged += (sender, e) => ViewModel = e.NewValue as GeneralSettingsViewModel;
    }
}
