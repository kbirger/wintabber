using CommunityToolkit.Mvvm.DependencyInjection;
using DynamicData.Binding;
using iNKORE.UI.WPF.DragDrop.Utilities;
using ReactiveUI;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinTabber.Events;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for WindowSelectorWindow.xaml
/// </summary>
public partial class WindowSelectorWindow : ReactiveWindow<WindowSelectorViewModel>
{
    private List<IDisposable> _resources = new();
    private WindowTileGrid? _tileGrid;

    /// <summary>Cached by <see cref="GetScreenBounds" />; cleared at the start of each open.</summary>
    private Rect? _screenBounds;

    private DpiScale _dpiScale;

    public static DependencyProperty MaxItemHeightProperty = DependencyProperty.Register(
        "MaxItemHeight",
        typeof(double),
        typeof(WindowSelectorWindow),
        new PropertyMetadata(400.0));
    public double MaxItemHeight
    {
        get { return (double)GetValue(MaxItemHeightProperty); }
        set
        {
            SetValue(MaxItemHeightProperty, value);
        }
    }

    public static DependencyProperty MaxItemWidthProperty = DependencyProperty.Register(
        "MaxItemWidth",
        typeof(double),
        typeof(WindowSelectorWindow),
        new PropertyMetadata(400.0));


    public double MaxItemWidth
    {
        get { return (double)GetValue(MaxItemWidthProperty); }
        set
        {
            SetValue(MaxItemWidthProperty, value);
        }
    }

    public WindowSelectorWindow()
    {
        InitializeComponent();
        _dpiScale = VisualTreeHelper.GetDpi(this);

        SizeChanged += MainWindow_SizeChanged;
        LayoutUpdated += MainWindow_LayoutUpdated;
        IsVisibleChanged += MainWindow_VisibilityChanged;
        DpiChanged += OnDpiChanged;


        var mgr = Ioc.Default.GetRequiredService<WinTabberEventManager>();
        _settings = Ioc.Default.GetRequiredService<SettingsViewModel>();
        _resources.Add(mgr);

        this.WhenActivated((dispose) =>
        {
            //    Debug.WriteLine("Activated");

            //    Disposable.Create(() => { Debug.WriteLine("Deactivated"); }).DisposeWith(dispose);
            //    this.OneWayBind(
            //        _settings.Appearance, 
            //        vm => vm.WindowTileWidthScaled, 
            //        view => view.MaxItemWidth
            //    ).DisposeWith(dispose);

            //    this.OneWayBind(
            //        _settings.Appearance,
            //        vm => vm.WindowTileWidthScaled,
            //        view => view.MaxItemHeight,
            //        (double width) =>
            //        {
            //            var ratio = WindowData.CursorScreen.Bounds.Height / WindowData.CursorScreen.Bounds.Width;
            //            return 55 + width * ratio;
            //        }
            //    ).DisposeWith(dispose);
            //});
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        var source = PresentationSource.FromVisual(this);
        _transformToDevice = source.CompositionTarget.TransformToDevice;
        _transformtoDip = source.CompositionTarget.TransformFromDevice;
        _settings.Appearance.WhenAnyPropertyChanged().Subscribe(_ =>
        {
            ScaleTiles();
            CenterWindow();
        });
        base.OnSourceInitialized(e);
    }

    private void MainWindow_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (bool.Equals(e.NewValue, false))
        {
            UpdateLayout();

        }

    }

    private void OnDpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        _dpiScale = e.NewDpi;
        // The logical bounds are derived from the DPI that just changed, so the cache is stale.
        _screenBounds = null;
        ScaleTiles();
        ApplyScreenBounds();
    }

    private void MainWindow_LayoutUpdated(object? sender, EventArgs e)
    {
        CenterWindow();
    }

    private unsafe void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        CenterWindow();
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var disposable in _resources)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }

    private void SwitchWindowAndClose()
    {
        if (Visibility == Visibility.Visible && WindowData.SelectedIndex >= 0 && WindowData.SelectedIndex < WindowData.WindowItems.Length)
        {
            WindowData.SelectedItem?.Activate();
        }
        WindowData.Deactivate();
        WindowData.EndPreview();
        WindowData.NotifySwitcherClosed();
        Hide();
        _tileGrid = null;
    }

    /// <summary>
    /// §5 fallback. If the trigger that opened the switcher carried no modifiers (e.g. a bare
    /// <c>F13</c> binding) there is no modifier release to commit on, so without this the switcher
    /// would be unclosable. Enter commits, Esc cancels.
    /// <para>
    /// Handled for every activation rather than only the modifier-less one: an always-available
    /// escape hatch is cheap, and "switcher stuck open" is the worst failure mode in this feature.
    /// </para>
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // A rename is in progress; Enter/Esc belong to the editor, not to the switcher.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                WindowData.CommitSelection();
                Hide();
                _tileGrid = null;
                return;
            case Key.Escape:
                e.Handled = true;
                WindowData.CancelSelection();
                Hide();
                _tileGrid = null;
                return;
        }

        base.OnPreviewKeyDown(e);
    }

    public WindowSelectorViewModel WindowData => (WindowSelectorViewModel)DataContext;

    protected override void OnActivated(EventArgs e)
    {
        ScaleTiles();
        CenterWindow();
        base.OnActivated(e);
    }

    //[MemberNotNull(nameof(_tileGrid))]
    //private void InitializeTileGrid()
    //{
    //    if (_tileGrid is not null)
    //    {
    //        return;
    //    }
    //    var infos = new List<WindowTileInfo>(TabListView.Items.Count);
    //    for (int i = 0; i < TabListView.Items.Count; i++)
    //    {
    //        var tile = GetTile(i);
    //        infos.Add(tile);
    //    }
    //    _tileGrid = WindowTileGrid.Create(infos);
    //}

    protected override void OnDeactivated(EventArgs e)
    {
        //SwitchWindowAndClose();

        base.OnDeactivated(e);
    }
    private const double FILL_PERCENT = 0.8;
    private const int MAX_ROW_LENGTH = 6;
    private void ScaleTiles()
    {
        //var f = PInvoke.GetProcessDpiAwareness(new SafeAccessTokenHandle(Process.GetCurrentProcess().Handle), out var value);
        //var source = PresentationSource.FromVisual(this);
        //_transformToDevice = source.CompositionTarget.TransformToDevice;
        //_transformtoDip = source.CompositionTarget.TransformFromDevice;
        var bounds = GetScreenBounds();
        var ratio = bounds.Height / bounds.Width;
        //MaxItemWidth = windowWidth / MAX_ROW_LENGTH;
        //MaxItemHeight = 55+  MaxItemWidth * ratio;

        MaxItemWidth = _settings.Appearance.WindowTileWidth * _settings.Appearance.ScaleFactor;
        // MaxItemHeight = 300;
        MaxItemHeight = 55+  MaxItemWidth * ratio;

    }


    private SettingsViewModel _settings;
    private Matrix _transformToDevice;
    private Matrix _transformtoDip;

    /// <summary>
    /// Logical bounds of the screen this open belongs to.
    /// <para>
    /// Resolved once per open and cached, because the underlying <c>CursorScreen</c> re-reads the
    /// live cursor on every access. Uncached, the tile sizing, the size constraints and the
    /// centering could each land on a different monitor if the pointer crossed a boundary
    /// mid-sequence — and <c>CenterWindow</c> runs from <c>LayoutUpdated</c>, so an open selector
    /// would chase the pointer onto whatever screen it wandered to.
    /// </para>
    /// </summary>
    private Rect GetScreenBounds()
    {
        if (_screenBounds is { } cached)
        {
            return cached;
        }

        var screen = WindowData.CursorScreen.Bounds;
        var logical = DpiHelper.DeviceRectToLogical(
            new Rect(screen.Left, screen.Top, screen.Width, screen.Height),
            _dpiScale.DpiScaleX,
            _dpiScale.DpiScaleY);

        _screenBounds = logical;
        return logical;
    }

    /// <summary>
    /// Clamp the window to its share of the screen.
    /// <para>
    /// Split out of <see cref="CenterWindow" /> so it can run <i>before</i> the first layout pass:
    /// available width decides how many tiles the <c>WrapPanel</c> puts on a row, so a MaxWidth
    /// landing after the window is already visible reflows every tile. Same failure that was fixed
    /// for tile size, one level up.
    /// </para>
    /// </summary>
    private void ApplyScreenBounds()
    {
        var bounds = GetScreenBounds();
        MaxHeight = bounds.Height * FILL_PERCENT;
        MaxWidth = bounds.Width * FILL_PERCENT;
    }

    /// <summary>
    /// Position only. Deliberately does <i>not</i> touch MaxWidth/MaxHeight: this runs from
    /// <c>LayoutUpdated</c>, and with <c>SizeToContent="WidthAndHeight"</c> writing a size
    /// constraint from inside a layout callback can re-trigger measure and reflow the tiles.
    /// <see cref="ApplyScreenBounds" /> owns the constraints and runs outside layout.
    /// </summary>
    private void CenterWindow()
    {
        var bounds = GetScreenBounds();

        Left = bounds.Left + (bounds.Width - ActualWidth) / 2;
        Top = bounds.Top + (bounds.Height - ActualHeight) / 2;
    }

    public void SelectWindow(int direction)
    {
        if (Visibility == Visibility.Visible)
        {
            WindowData.EndPreview();
            CenterWindow();
            //ChangeSelection(direction);
            return;
        }

        //WindowData.Activate();
        //UpdateLayout();
        //Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        //Arrange(new Rect(DesiredSize));
        //UpdateLayout();
        //SizeToContent = SizeToContent.Manual; // lock size
        //Show();
        //SizeToContent = SizeToContent.WidthAndHeight;
        //Focus();
        //Activate();
        //TabListView.Focus();
    }

    public void ShowWindowSelector()
    {
        // Before Show(), not after. MaxItemWidth/MaxItemHeight default to 400 and are only
        // given their real values here; leaving this to OnActivated (which Activate() raises
        // below) meant the first layout pass ran with 400x400 tiles and the window visibly
        // resized once the settings-derived size landed.
        // Resolve which screen this open belongs to once, up front; everything below reads it.
        _screenBounds = null;
        var bounds = GetScreenBounds();

        ScaleTiles();

        // Size constraints too: the WrapPanel's items-per-row depends on the available width, so
        // a MaxWidth arriving after Show() rearranges every tile in front of the user.
        ApplyScreenBounds();

        TabListView.SuppressHoverUntilPointerMoves();

        // Park directly above the target screen, so layout can settle without a half-built frame
        // being visible if one does slip through. Horizontally aligned with that screen so the
        // window stays associated with it and doesn't pick up a neighbour's DPI on the way back.
        Left = bounds.Left;
        Top = bounds.Top - bounds.Height;
        Show();
        // Show() on a reused window only invalidates measure; force the pass to completion so
        // CenterWindow reads a settled ActualWidth/ActualHeight rather than the previous open's.
        UpdateLayout();
        CenterWindow();

        Focus();
        Activate();
        TabListView.Focus();

    }

    private void Grid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (IsUnderEditableTextBlock(e.OriginalSource))
        {
            // The click landed on a button (suspend/accept/cancel) inside the tab-title component;
            // don't also switch windows and close the selector out from under it.
            return;
        }

        // Take the selection from the tile that was actually clicked. This handler is wired to
        // MouseUp, which fires for any button, but a ListViewItem only selects itself on a *left*
        // button down - so a middle- or right-click here would otherwise activate whatever was
        // selected before. Also covers a left-click landing before hover selection is re-armed.
        if (sender is FrameworkElement { DataContext: WindowItem clicked })
        {
            WindowData.SelectedItem = clicked;
        }

        SwitchWindowAndClose();
    }

    /// <summary>
    /// Walks up the visual tree from <paramref name="originalSource"/> looking for an <see cref="EditableTextBlock"/>.
    /// WPF's MouseUp/MouseLeftButtonUp promotion means a button click's bubbled event can't be relied upon to be
    /// swallowed by the button itself, so this is checked explicitly.
    /// </summary>
    private static bool IsUnderEditableTextBlock(object? originalSource)
    {
        if (originalSource is not DependencyObject node)
        {
            return false;
        }

        while (node is not null)
        {
            if (node is EditableTextBlock)
            {
                return true;
            }
            // OriginalSource can be a content element (e.g. a Run); VisualTreeHelper throws on those.
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }
}