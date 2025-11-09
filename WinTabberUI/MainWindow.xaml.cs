using CommunityToolkit.Mvvm.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public MainWindow()
    {
        InitializeComponent();
        WindowData = Ioc.Default.GetRequiredService<WindowSelectorViewModel>();
        DataContext = WindowData;
        SizeChanged += MainWindow_SizeChanged;
        LayoutUpdated += MainWindow_LayoutUpdated;
        

        var mgr = Ioc.Default.GetRequiredService<WinTabberEventManager>();
        _resources.Add(mgr);

        ArgumentNullException.ThrowIfNull(SynchronizationContext.Current);
        mgr.CommandEvents
            .ObserveOn(SynchronizationContext.Current)
            .Subscribe(e =>
            {
                switch (e.Type)
                {
                    case EventType.CmdNextWindow:
                        //SelectWindow(1);
                        break;
                    case EventType.CmdPreviousWindow:
                        //SelectWindow(-1);
                        break;
                    case EventType.CmdAppHide:
                        if(WindowData.SelectedItem is null || !WindowData.SelectedItem.IsEditing)
                        {
                            //SwitchWindowAndClose();
                        }
                        break;
                    case EventType.CmdMinimizeWindow:
                        WindowData.WindowManager.CurrentWindow()?.Minimize();
                        break;
                    case EventType.CmdMaximizeWindow:
                        WindowData.WindowManager.CurrentWindow()?.Maximize();
                        break;
                }
            });        
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
            //Thread.Sleep(100);
            WindowData.SelectedItem.Activate();
        }
        WindowData.Deactivate();
        WindowData.EndPreview();
        Hide();
        _tileGrid = null;
    }

    public WindowSelectorViewModel WindowData { get; private set; }

    protected override void OnActivated(EventArgs e)
    {
        var dpiInfo = VisualTreeHelper.GetDpi(this);

        MaxItemHeight = dpiInfo.DpiScaleY * 300;

        base.OnActivated(e);
    }

    [MemberNotNull(nameof(_tileGrid))]
    private void InitializeTileGrid()
    {
        if(_tileGrid is not null)
        {
            return;
        }
        var infos = new List<WindowTileInfo>(TabListView.Items.Count);
        for (int i = 0; i < TabListView.Items.Count; i++)
        {
            var tile = GetTile(i);
            infos.Add(tile);
        }
        _tileGrid = WindowTileGrid.Create(infos);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        //SwitchWindowAndClose();
        base.OnDeactivated(e);
    }

    private void CenterWindow()
    {
        var screenCenter = WindowData.CenterScreen;
        var dpiInfo = VisualTreeHelper.GetDpi(this);
        var scale = dpiInfo.DpiScaleX;
        MaxWidth = WindowData.CursorScreen.Bounds.Width * .6 * scale;
        MaxHeight = WindowData.CursorScreen.Bounds.Height * .6 * scale;

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

    private void ChangeSelection(int direction)
    {
        WindowData.SelectedIndex += direction;
    }

    private void TabListView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var lv = TabListView;
        //lv.Focus();

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if(key == Key.LeftCtrl)
        {
            //WindowData.PreviewSelectedWindow();
            return;
        }



        if (!new Key[] { Key.Down, Key.Up, Key.Left, Key.Right }.Contains(key))
        {
            return;
        }
        InitializeTileGrid();

        var next = key switch
        {
            Key.Down => _tileGrid.MoveDown(),
            Key.Up => _tileGrid.MoveUp(),
            Key.Left => _tileGrid.MoveLeft(),
            Key.Right => _tileGrid.MoveRight(),
            _ => null
        };

        if (next is { })
        {
            lv.SelectedItem = next;
            e.Handled = true;
        }
    }

    private WindowTileInfo GetTile(int index)
    {
        var container = (Visual)TabListView.ItemContainerGenerator.ContainerFromIndex(index);
        var item = WindowData.WindowItems[index];
        var location = container.TransformToVisual(TabListView).Transform(new Point(0, 0));

        return new WindowTileInfo
        {
            Container = container,
            WindowItem = item,
            Location = location,
            IsSelected = index == TabListView.SelectedIndex,
            Index = index
        };
    }

    private void TabListView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        //SwitchWindowAndClose();
    }

    private void TabListView_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        //if(e.SystemKey == Key.LeftCtrl)
        //{
        //    WindowData.EndPreview();
        //}
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        RenameWindow.ShowFor(((FrameworkElement)e.Source).DataContext as WindowItem);
    }

    private void EditableTextBlock_TextChanged(object sender, TextUpdatedEventArgs e)
    {
        WindowData.SelectedItem.Title = e.NewValue;
    }

    private void Grid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        SwitchWindowAndClose();
    }

    private void TabListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is WindowItem windowItem)
        {
            WindowData.SelectedItem = windowItem;
            TabListView.ScrollIntoView(windowItem);
        }
    }
}