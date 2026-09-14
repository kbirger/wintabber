using ReactiveUI;
using WinTabber.ViewModels;

namespace WinTabberUI.Views;

/// <summary>
/// See GeneralSettingsPageBase.cs for the full rationale: WinUI 3's XAML compiler does not
/// propagate x:TypeArguments from a generic base class on a XAML root (CS0305); this
/// non-generic intermediate class is the documented ReactiveUI.WinUI workaround. Unlike the
/// settings pages, EditableTextBlock's ViewModel is bound directly (x:Bind ViewModel="{Binding}"
/// from the hosting ItemTemplate), not via DataContext, so this base class does not need the
/// DataContextChanged wiring GeneralSettingsPageBase has — ViewModel is set explicitly per
/// instance instead. See Task 4b.4's item template for how.
/// </summary>
public class EditableTextBlockBase : ReactiveUserControl<WindowItem>
{
}
