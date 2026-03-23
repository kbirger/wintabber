using CommunityToolkit.Mvvm.DependencyInjection;
using DynamicData.Binding;
using iNKORE.UI.WPF.DragDrop.Utilities;
using Microsoft.Win32.SafeHandles;
using ReactiveUI;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml.Linq;
using Windows.UI.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinTabber.Events;
using WinTabberUI.ViewModels;
using static WinTabberUI.EditableTextBlock;

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
        Hide();
        _tileGrid = null;
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