using CommunityToolkit.Mvvm.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.UI.Core;
using WinTabber.Events;
using WinTabberUI.ViewModels;
using static WinTabberUI.EditableTextBlock;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private List<IDisposable> _resources = new();
    private WindowTileGrid? _tileGrid;

    private DpiScale _dpiScale;

    public static DependencyProperty MaxItemHeightProperty = DependencyProperty.Register(
        "MaxItemHeight",
        typeof(double),
        typeof(MainWindow),
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
        typeof(MainWindow),
        new PropertyMetadata(400.0));
    public double MaxItemWidth
    {
        get { return (double)GetValue(MaxItemWidthProperty); }
        set
        {
            SetValue(MaxItemWidthProperty, value);
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        _dpiScale = VisualTreeHelper.GetDpi(this);
        WindowData = Ioc.Default.GetRequiredService<WindowSelectorViewModel>();
        DataContext = WindowData;
        SizeChanged += MainWindow_SizeChanged;
        LayoutUpdated += MainWindow_LayoutUpdated;
        IsVisibleChanged  += MainWindow_VisibilityChanged;
        DpiChanged += OnDpiChanged;


        var mgr = Ioc.Default.GetRequiredService<WinTabberEventManager>();
        _resources.Add(mgr);

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
        Hide();
        _tileGrid = null;
    }

    public WindowSelectorViewModel WindowData { get; private set; }

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
    private const int MAX_ROW_LENGTH = 5;
    private void ScaleTiles()
    {
        var scaleX = _dpiScale.DpiScaleX;
        var scaleY = _dpiScale.DpiScaleY;
        var screenWidth = WindowData.CursorScreen.Bounds.Width / scaleX;
        var screenHeight = WindowData.CursorScreen.Bounds.Height / scaleY;
        var ratio = screenHeight / screenWidth;
        var windowWidth = screenWidth * FILL_PERCENT;
        var windowHeight = screenHeight * FILL_PERCENT;
        MaxItemWidth = windowWidth / MAX_ROW_LENGTH;
        MaxItemHeight = 24 + windowWidth * ratio / MAX_ROW_LENGTH;
        // MaxItemWidth = ;
        // MaxItemHeight = 300;
    }

    double W = 1200;
    private void CenterWindow()
    {
        // if (!IsVisible)
        // {
        //     return;
        // }
        var screenCenter = WindowData.CenterScreen;
        var scale = _dpiScale.DpiScaleX;
        // SizeToContent = SizeToContent.Manual;
        // Width = W;
        MaxHeight = WindowData.CursorScreen.Bounds.Height / scale * FILL_PERCENT;
        MaxWidth = WindowData.CursorScreen.Bounds.Width / scale * FILL_PERCENT;

        Left = WindowData.CursorScreen.Bounds.Left * scale + (WindowData.CursorScreen.Bounds.Width / scale - ActualWidth) / 2;
        Top = WindowData.CursorScreen.Bounds.Top * scale + (WindowData.CursorScreen.Bounds.Height / scale - ActualHeight) / 2;
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
        Top = -1000;
        Show();
        Focus();
        Activate();
        TabListView.Focus();

    }

    private void Grid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        SwitchWindowAndClose();
    }
}