using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTabber.Events.Shortcuts;

namespace WinTabber.UI.Common.Controls;

/// <summary>
/// Read-only renderer for a <see cref="ShortcutTrigger" />. Chip rendering lives here and nowhere
/// else — <see cref="ShortcutCaptureBox" /> hosts this control rather than duplicating it.
/// </summary>
public class ShortcutPresenter : Control
{
    public ShortcutPresenter()
    {
        DefaultStyleKey = typeof(ShortcutPresenter);
    }

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger),
        typeof(ShortcutTrigger),
        typeof(ShortcutPresenter),
        new PropertyMetadata(null, OnVisualInputChanged)
    );

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(ShortcutPresenter),
        new PropertyMetadata(Orientation.Horizontal)
    );

    public static readonly DependencyProperty ShowEdgeHintProperty = DependencyProperty.Register(
        nameof(ShowEdgeHint),
        typeof(bool),
        typeof(ShortcutPresenter),
        new PropertyMetadata(true, OnVisualInputChanged)
    );

    public static readonly DependencyProperty ChipsProperty = DependencyProperty.Register(
        nameof(Chips),
        typeof(IReadOnlyList<ShortcutChip>),
        typeof(ShortcutPresenter),
        new PropertyMetadata(Array.Empty<ShortcutChip>())
    );

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
        nameof(IsEmpty),
        typeof(bool),
        typeof(ShortcutPresenter),
        new PropertyMetadata(true, OnIsEmptyChanged)
    );

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText),
        typeof(string),
        typeof(ShortcutPresenter),
        new PropertyMetadata("Not set")
    );

    public ShortcutTrigger? Trigger
    {
        get => (ShortcutTrigger?)GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Renders a trailing "release" chip for a <see cref="TriggerEdge.Release" /> trigger.</summary>
    public bool ShowEdgeHint
    {
        get => (bool)GetValue(ShowEdgeHintProperty);
        set => SetValue(ShowEdgeHintProperty, value);
    }

    public IReadOnlyList<ShortcutChip> Chips => (IReadOnlyList<ShortcutChip>)GetValue(ChipsProperty);

    /// <summary>True when there is nothing to render, so the template can show <see cref="EmptyText" />.</summary>
    public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    // WinUI 3's VisualStateManager callbacks only fire on a property *change*, not at template
    // application — unlike WPF's declarative Style.Triggers, which also matched at the property's
    // default value. Without this override, a presenter whose Trigger is never set (IsEmpty stays
    // at its default true) never enters the "Empty" state and PART_Empty stays hidden.
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        VisualStateManager.GoToState(this, IsEmpty ? "Empty" : "HasChips", false);
    }

    private static void OnVisualInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ShortcutPresenter)d).Rebuild();

    // WPF's original used a Trigger Property="IsEmpty" that fired automatically off the dependency
    // property; WinUI 3's VisualStateManager needs an explicit GoToState call instead.
    private static void OnIsEmptyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        VisualStateManager.GoToState((ShortcutPresenter)d, (bool)e.NewValue ? "Empty" : "HasChips", true);

    private void Rebuild()
    {
        var chips = ShortcutChips.Build(Trigger, ShowEdgeHint);
        SetValue(ChipsProperty, chips);
        SetValue(IsEmptyProperty, chips.Count == 0);
    }
}
