// winui3/WinTabberUI/Views/WindowSelectorWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using WinTabber.ViewModels;
using WinTabberUI.Models.Settings;
using WinTabberUI.Windowing;
using WinUIEx;

namespace WinTabberUI.Views;

// Two real findings from this port worth flagging for whoever touches this file (or the next
// Window-rooted XAML in this migration) next:
//   1. `{x:Bind Foo, Converter={StaticResource Bar}}` cannot be used anywhere in this file's XAML --
//      DataTemplate-scoped or top-level -- because the generated binding code always ends up calling
//      SetConverterLookupRoot(this), and `this` here is WindowSelectorWindow : WinUIEx.WindowEx, which
//      is not a Microsoft.UI.Xaml.FrameworkElement (confirmed via real compiler output: CS1503 "cannot
//      convert from 'WinTabberUI.Views.WindowSelectorWindow' to 'Microsoft.UI.Xaml.FrameworkElement'").
//      Every converter-carrying binding in WindowSelectorWindow.xaml therefore uses classic {Binding}
//      instead (DataContext resolved via RootGrid.DataContext set below, or via ElementName for the
//      DataTemplate-scoped tile sizing -- see TileSize's doc comment). x:Bind without a converter is
//      unaffected and used throughout.
//   2. A large multi-line XML comment placed directly before the root Grid element in this file's XAML
//      reproducibly made XamlCompiler.exe exit 1 with zero output on both stdout/stderr and in
//      output.json's MSBuildLogEntries -- the same "no diagnostic at all" pass2 crash class
//      SettingsWindow.xaml.cs's SystemBackdrop finding already documented, but triggered by comment
//      content this time, confirmed by bisection (removing just that comment, with no other change,
//      made the build succeed). Keep XAML comments in this file short; put detailed rationale in this
//      .cs file instead, as this comment block does.
public sealed partial class WindowSelectorWindow : WindowEx
{
    private readonly nint _hwnd;
    private readonly ApplicationSettings _settings;
    private Rect? _screenBounds;
    private bool _parked;
    private int _framesBeforeReveal;

    public WindowSelectorViewModel ViewModel { get; }

    // DEVIATION from the brief's stated "Produces" constructor shape
    // (WindowSelectorWindow(WindowSelectorViewModel viewModel)): a second parameter, ApplicationSettings,
    // was added to resolve TODO(verify) item 3 (ScaleTiles' settings property path) against a real,
    // already-DI-registered singleton (Bootstrapper.AddSettingsGraph) rather than threading the whole
    // SettingsViewModel through (the WPF original's approach) or reaching for a service locator. DI
    // resolves the extra parameter with no further wiring since ApplicationSettings is already
    // registered as a singleton.
    public WindowSelectorWindow(WindowSelectorViewModel viewModel, ApplicationSettings settings)
    {
        ViewModel = viewModel;
        _settings = settings;
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Only needed for the one binding that could not stay {x:Bind} -- see
        // OnRootGridPreviewKeyDown's neighboring CloseApplicationButton.Visibility doc note in the
        // .xaml file for why (CS1503 on SetConverterLookupRoot(this): WindowEx is not a
        // FrameworkElement). Classic {Binding} resolves this via inherited DataContext down the
        // visual tree from RootGrid, exactly like it does in WPF/UWP.
        RootGrid.DataContext = ViewModel;

        // REAL BUG found via live verification (Task 4b.4), not in the brief's draft at all: without
        // this, WindowThumbnail.TargetWindow is never set for any tile, so InitialiseThumbnail's
        // `TargetWindow is { } window` guard is always false and DwmRegisterThumbnail never runs --
        // thumbnails would silently never appear. Worse, it also produced a real crash during this
        // task's live testing: a COMException (E_FAIL) out of WindowThumbnail.MeasureOverride's
        // DwmQueryThumbnailSourceSize call, surfaced only once containers were actually realized under
        // repeated ItemsSource churn. Ported from DockWindow.xaml.cs's identical wiring/rationale
        // (FindName does not resolve a DataTemplate's realized content as a name scope in WinUI 3, so a
        // VisualTreeHelper walk for the first WindowThumbnail descendant is used instead of the brief's
        // unstated assumption that Source alone would be enough).
        TabListView.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer.ContentTemplateRoot is FrameworkElement root
                && FindWindowThumbnail(root) is { } thumbnail)
            {
                thumbnail.TargetWindow = this;
            }
        };

        RootGrid.SizeChanged += (_, _) => CenterWindow();
        Activated += (_, _) => { ScaleTiles(); CenterWindow(); };
        Closed += (_, _) => DisarmReveal();
    }

    // See the ContainerContentChanging wiring above for why this walk is needed instead of FindName.
    private static WinTabberUI.Controls.WindowThumbnail? FindWindowThumbnail(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is WinTabberUI.Controls.WindowThumbnail thumbnail)
            {
                return thumbnail;
            }

            if (FindWindowThumbnail(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Frames still to be composed before revealing. Two rather than one because
    /// CompositionTarget.Rendering is raised while a frame is still being built, not after it has
    /// been presented -- ported from the WPF original's identical comment.
    /// <para>
    /// RESOLVED (TODO(verify) item 1/2): confirmed via real compiler output that
    /// Microsoft.UI.Xaml.Media.CompositionTarget.Rendering is EventHandler&lt;object&gt; (not plain
    /// EventHandler like WPF's), so RevealWhenComposed's signature below is `object? sender, object e`.
    /// The Dispatcher.BeginInvoke(DispatcherPriority.Render, ...) fallback is NOT ported: WinUI 3's
    /// DispatcherQueue has no Render-specific priority (only High/Normal/Low -- confirmed, no
    /// intermediate "after layout, before input" priority exists), and unlike the fallback's WPF
    /// justification, this window is NOT reused across opens (a fresh transient instance is resolved
    /// per open per Bootstrapper's `.AddTransient&lt;Views.WindowSelectorWindow&gt;()` registration --
    /// see the DI registration comment), so the stale-surface-reuse problem the fallback guarded
    /// against does not apply here structurally, not just probabilistically. If a future change makes
    /// this window a reused singleton, revisit this.
    /// </para>
    /// </summary>
    private void ArmReveal()
    {
        _parked = true;
        _framesBeforeReveal = 2;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= RevealWhenComposed;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += RevealWhenComposed;
    }

    private void DisarmReveal()
    {
        _parked = false;
        _framesBeforeReveal = 0;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= RevealWhenComposed;
    }

    private void RevealWhenComposed(object? sender, object e)
    {
        if (--_framesBeforeReveal > 0)
        {
            return;
        }

        RevealNow();
    }

    private void RevealNow()
    {
        if (!_parked)
        {
            return;
        }

        DisarmReveal();
        CenterWindow();
    }

    private const double FillPercent = 0.8;

    /// <summary>
    /// RESOLVED (TODO(verify) item 3): WPF's WrapPanel had ItemWidth/ItemHeight for uniform tile
    /// sizing; CommunityToolkit.WinUI.Controls.WrapPanel has no such properties (confirmed -- its
    /// public surface is only Orientation/HorizontalSpacing/VerticalSpacing), so uniform sizing is
    /// reproduced on the tile Grid itself via MaxWidth/MaxHeight, using the same
    /// `_settings.Appearance.WindowTileWidth * _settings.Appearance.ScaleFactor` formula and
    /// height-from-aspect-ratio calculation as the WPF original's ScaleTiles/MaxItemWidth/MaxItemHeight.
    /// Pushed onto RootGrid via the TileSize attached properties below (see that class's doc comment
    /// for why an ElementName binding to an attached property on RootGrid, rather than x:Bind against
    /// the Window itself, which is not a DependencyObject/FrameworkElement Windows can even name-scope
    /// bind to reliably here).
    /// </summary>
    private void ScaleTiles()
    {
        var bounds = GetScreenBounds();
        var ratio = bounds.Width > 0 ? bounds.Height / bounds.Width : 1.0;
        var width = _settings.Appearance.WindowTileWidth * _settings.Appearance.ScaleFactor;

        TileSize.SetMaxItemWidth(RootGrid, width);
        TileSize.SetMaxItemHeight(RootGrid, 55 + width * ratio);
    }

    private Rect GetScreenBounds()
    {
        if (_screenBounds is { } cached)
        {
            return cached;
        }

        var cursorScreenBounds = ViewModel.CursorScreen.Bounds;
        var rect = DesktopHelper.ToLogicalBounds(_hwnd, new System.Drawing.Rectangle(
            cursorScreenBounds.X, cursorScreenBounds.Y, cursorScreenBounds.Width, cursorScreenBounds.Height));
        var logical = new Rect(rect.X, rect.Y, rect.Width, rect.Height);

        _screenBounds = logical;
        return logical;
    }

    private void ApplyScreenBounds()
    {
        var bounds = GetScreenBounds();
        MaxHeight = bounds.Height * FillPercent;
        MaxWidth = bounds.Width * FillPercent;
    }

    private void CenterWindow()
    {
        if (_parked)
        {
            return;
        }

        var bounds = GetScreenBounds();
        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        var x = bounds.Left + (bounds.Width - Bounds.Width) / 2;
        var y = bounds.Top + (bounds.Height - Bounds.Height) / 2;

        AppWindow.Move(new Windows.Graphics.PointInt32((int)(x * scale), (int)(y * scale)));
    }

    public void ShowWindowSelector()
    {
        _screenBounds = null;
        var bounds = GetScreenBounds();

        ScaleTiles();
        ApplyScreenBounds();

        TabListView.SuppressHoverUntilPointerMoves();

        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        AppWindow.Move(new Windows.Graphics.PointInt32(
            (int)(bounds.Left * scale), (int)((bounds.Top - bounds.Height) * scale)));

        ArmReveal();
        Activate();
        TabListView.Focus(FocusState.Programmatic);
    }

    public void SwitchWindowAndClose()
    {
        if (ViewModel.SelectedIndex >= 0 && ViewModel.SelectedIndex < ViewModel.WindowItems.Length)
        {
            ViewModel.SelectedItem?.Activate();
        }

        // RESTORED: the brief's draft dropped this call (present in the WPF original's
        // SwitchWindowAndClose). Without it, a mouse-click close leaves ViewModel.SelectedIndex at
        // whatever the clicked tile's index was instead of resetting to -1, which would make the next
        // open's SelectNext/SelectPrevious skip RefreshOnActivation's RefreshFromForeground call (it
        // only refreshes when SelectedIndex < 0) -- the switcher would then open showing a stale tile
        // list from before the close, not the freshly-focused application's windows.
        ViewModel.Deactivate();
        ViewModel.EndPreview();
        ViewModel.NotifySwitcherClosed();
        this.Hide();
    }

    private void OnTileGridPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsUnderEditableTextBlock(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: WindowItem clicked })
        {
            ViewModel.SelectedItem = clicked;
        }

        SwitchWindowAndClose();
    }

    private static bool IsUnderEditableTextBlock(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is EditableTextBlock)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    /// <summary>
    /// §5 fallback: Enter commits, Esc cancels. Ported from the WPF original's
    /// OnPreviewKeyDown(KeyEventArgs) Window override.
    /// <para>
    /// RESOLVED (TODO(verify) item 4, and the reason this is a RootGrid.PreviewKeyDown event handler
    /// rather than an OnKeyDown override): Microsoft.UI.Xaml.Window / WinUIEx.WindowEx is not a
    /// DependencyObject/UIElement (confirmed by DockWindow.xaml.cs's "WinUI 3's Window has no
    /// overridable OnClosed" precedent, and independently confirmed here: `protected override void
    /// OnKeyDown` as drafted in the brief fails with CS0115 "no suitable method found to override"
    /// against the real compiler -- WinUIEx.WindowEx's public surface has no OnKeyDown/OnPreviewKeyDown
    /// virtual at all), so there is no OnKeyDown to override -- WindowSelectorWindow.xaml wires this
    /// to RootGrid's tunneling PreviewKeyDown event instead, the same event-vs-override substitution
    /// SpatialNavigationListView already established for its own arrow-key handling. RootGrid is the
    /// root of the content tree, so its PreviewKeyDown fires before any descendant's (including
    /// TabListView's own PreviewKeyDown, which only acts on arrow keys and leaves Enter/Escape alone).
    /// </para>
    /// <para>
    /// The WPF original's `Keyboard.FocusedElement is TextBox` guard is ported as
    /// `FocusManager.GetFocusedElement(...) is TextBox`: a rename-in-progress's Enter/Escape belong to
    /// EditableTextBlock's own TextBox KeyDown handler (Task 4b.2), not to this window-level handler --
    /// without this guard, RootGrid's tunneling PreviewKeyDown would set e.Handled=true and stop the
    /// event before it ever reached the TextBox's own (bubbling) KeyDown, silently breaking rename
    /// commit/cancel.
    /// </para>
    /// </summary>
    private void OnRootGridPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Content?.XamlRoot is { } xamlRoot && FocusManager.GetFocusedElement(xamlRoot) is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                ViewModel.CommitSelection();
                this.Hide();
                return;
            case VirtualKey.Escape:
                e.Handled = true;
                ViewModel.CancelSelection();
                this.Hide();
                return;
        }
    }
}

/// <summary>
/// Live-updatable per-tile size constraints for WindowSelectorWindow's ItemTemplate, pushed by
/// ScaleTiles() and read via an ElementName+attached-property classic Binding from the tile Grid
/// (see WindowSelectorWindow.xaml). Not an x:Bind against the Window itself: WinUIEx.WindowEx is not
/// a DependencyObject/FrameworkElement (see ScaleTiles' and OnRootGridPreviewKeyDown's doc comments),
/// so it cannot be an x:Bind or classic-Binding ElementName target at all -- RootGrid (a real,
/// named FrameworkElement) hosts these two attached properties instead, and the tile Grid binds to
/// them by path. Public (not internal) so the classic Binding engine's reflection-based property
/// path resolution can see the static accessors from XAML at runtime.
/// </summary>
public static class TileSize
{
    public static readonly DependencyProperty MaxItemWidthProperty = DependencyProperty.RegisterAttached(
        "MaxItemWidth", typeof(double), typeof(TileSize), new PropertyMetadata(400.0));

    public static readonly DependencyProperty MaxItemHeightProperty = DependencyProperty.RegisterAttached(
        "MaxItemHeight", typeof(double), typeof(TileSize), new PropertyMetadata(400.0));

    public static double GetMaxItemWidth(DependencyObject obj) => (double)obj.GetValue(MaxItemWidthProperty);

    public static void SetMaxItemWidth(DependencyObject obj, double value) => obj.SetValue(MaxItemWidthProperty, value);

    public static double GetMaxItemHeight(DependencyObject obj) => (double)obj.GetValue(MaxItemHeightProperty);

    public static void SetMaxItemHeight(DependencyObject obj, double value) => obj.SetValue(MaxItemHeightProperty, value);
}
