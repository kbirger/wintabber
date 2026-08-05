using System.Windows;
using System.Windows.Controls;
using WinTabber.Events.Shortcuts;

namespace WinTabber.UI.Common.Controls;

/// <summary>
/// Read-only renderer for a <see cref="ShortcutTrigger" />. Chip rendering lives here and nowhere
/// else — <see cref="ShortcutCaptureBox" /> hosts this control rather than duplicating it.
/// </summary>
public class ShortcutPresenter : Control
{
    static ShortcutPresenter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ShortcutPresenter),
            new FrameworkPropertyMetadata(typeof(ShortcutPresenter))
        );
    }

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger),
        typeof(ShortcutTrigger),
        typeof(ShortcutPresenter),
        new FrameworkPropertyMetadata(null, OnVisualInputChanged)
    );

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(ShortcutPresenter),
        new FrameworkPropertyMetadata(Orientation.Horizontal)
    );

    public static readonly DependencyProperty ShowEdgeHintProperty = DependencyProperty.Register(
        nameof(ShowEdgeHint),
        typeof(bool),
        typeof(ShortcutPresenter),
        new FrameworkPropertyMetadata(true, OnVisualInputChanged)
    );

    private static readonly DependencyPropertyKey ChipsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Chips),
        typeof(IReadOnlyList<ShortcutChip>),
        typeof(ShortcutPresenter),
        new FrameworkPropertyMetadata(Array.Empty<ShortcutChip>())
    );

    public static readonly DependencyProperty ChipsProperty = ChipsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsEmptyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEmpty),
        typeof(bool),
        typeof(ShortcutPresenter),
        new FrameworkPropertyMetadata(true)
    );

    public static readonly DependencyProperty IsEmptyProperty = IsEmptyPropertyKey.DependencyProperty;

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText),
        typeof(string),
        typeof(ShortcutPresenter),
        new FrameworkPropertyMetadata("Not set")
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

    private static void OnVisualInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ShortcutPresenter)d).Rebuild();

    private void Rebuild()
    {
        var chips = ShortcutChips.Build(Trigger, ShowEdgeHint);
        SetValue(ChipsPropertyKey, chips);
        SetValue(IsEmptyPropertyKey, chips.Count == 0);
    }
}
