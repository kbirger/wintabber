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
        ScaleTiles();
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
        var origBounds = new Vector(WindowData.CursorScreen.Bounds.Width, WindowData.CursorScreen.Bounds.Height);
        //var dipBounds = _transformtoDip.Transform(origBounds);
        //var screenBounds2 = _transformToDevice.Transform(origBounds);
        var ratio = origBounds.Y / origBounds.X;
        var bounds = origBounds;
        var windowWidth = bounds.X * FILL_PERCENT;
        var windowHeight = bounds.Y * FILL_PERCENT;
        //MaxItemWidth = windowWidth / MAX_ROW_LENGTH;
        //MaxItemHeight = 55+  MaxItemWidth * ratio;

        MaxItemWidth = _settings.Appearance.WindowTileWidth * _settings.Appearance.ScaleFactor;
        // MaxItemHeight = 300;
        MaxItemHeight = 55+  MaxItemWidth * ratio;

    }


    private SettingsViewModel _settings;
    private Matrix _transformToDevice;
    private Matrix _transformtoDip;

    private void CenterWindow()
    {
        // if (!IsVisible)
        // {
        //     return;
        // }
        //var screenCenter = WindowData.CenterScreen;
        var scale = 1;// _dpiScale.DpiScaleX;
        var screen = WindowData.CursorScreen.Bounds;
        var screen2 = new Rect(screen.Left, screen.Top, screen.Width, screen.Height);
        //var bounds = VisualTreeHelper.GetTransform(this).TransformBounds();
        var bounds = DpiHelper.DeviceRectToLogical(screen2, _dpiScale.DpiScaleX, _dpiScale.DpiScaleY);
        // SizeToContent = SizeToContent.Manual;
        // Width = W;
        MaxHeight = bounds.Height / scale * FILL_PERCENT;
        MaxWidth = bounds.Width / scale * FILL_PERCENT;

        Left = bounds.Left * scale + (bounds.Width / scale - ActualWidth) / 2;
        Top = bounds.Top * scale + (bounds.Height / scale - ActualHeight) / 2;
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
        ScaleTiles();
        Top = -1000;
        Show();
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