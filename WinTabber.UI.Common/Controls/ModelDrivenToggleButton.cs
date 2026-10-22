using Microsoft.UI.Xaml.Controls.Primitives;

namespace WinTabber.UI.Common.Controls;

/// <summary>
/// A ToggleButton whose checked state comes only from its binding. A click runs the command but
/// does not flip IsChecked itself. The default toggle writes a local value that replaces the
/// one-way binding, so the glyph stops following the model and shows the wrong state.
/// </summary>
public class ModelDrivenToggleButton : ToggleButton
{
    protected override void OnToggle() { }
}
