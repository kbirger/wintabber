using ReactiveUI;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

// WinUI 3's XAML compiler does not propagate x:TypeArguments from a generic base class onto the
// generated partial class (microsoft/microsoft-ui-xaml#7746, closed as not planned) — the generated
// code-behind ends up as `partial class GeneralSettingsPage : ReactiveUI.ReactivePage` with no type
// argument, which fails to compile (CS0305). The standard workaround is this non-generic
// intermediate base class: GeneralSettingsPage.xaml roots on GeneralSettingsPageBase instead of
// rxwpf:ReactivePage directly, so the XAML compiler only ever sees a closed (non-generic) base type.
public class GeneralSettingsPageBase : ReactivePage<GeneralSettingsViewModel> { }
