using GlobalHotKeys;
using GlobalHotKeys.Native.Types;
using Gma.System.MouseKeyHook;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsInput.Events;
using WinTabber.API;
using WinTabber.Events;
using WinTabber.Interop;
using System.Reactive.Threading.Tasks;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public record MouseShortcut(MouseButtons mouseButton, bool alt, bool ctrl, bool shift, bool windows);
        public WindowManager WindowManager { get; } = new(new InteropProxy());
        private List<IDisposable> _resources = new();

        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        public enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        public enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }
        public MainWindow()
        {
            InitializeComponent();


            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            LostFocus += MainWindow_LostFocus;
            var mgr = WinTabberEventManager.Create();
            _resources.Add(mgr);

            mgr.Events.Subscribe(e =>

                Dispatcher.InvokeAsync(() =>
                {
                    //Console.WriteLine(e);
                    //Debug.WriteLine(e);

                    switch (e.type)
                    {
                        case EventType.NextWindow:
                            SelectWindow(1);
                            break;
                        case EventType.PreviousWindow:
                            SelectWindow(-1);
                            break;
                        case EventType.AppHide:
                            SwitchWindowAndClose();
                            break;
                        case EventType.MinimizeWindow:
                            WindowManager.CurrentWindow()?.Minimize();
                            break;
                        case EventType.MaximizeWindow:
                            WindowManager.CurrentWindow()?.Maximize();
                            break;
                        case EventType.ForegroundChanged:
                            WindowManager.RegisterForegroundWindowChanged((int)e.data!);
                            break;
                    }

                    return e;
                }).Task.ToObservable()
            );
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            CenterWindow();
        }

        private void MainWindow_LostFocus(object sender, RoutedEventArgs e)
        {
            //SwitchWindowAndClose();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CenterWindow();
            var h = new WindowInteropHelper(this).Handle;
            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
            accent.GradientColor = (100 << 24) | (0x000000 & 0xffffff);
            var accentSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentSize;
            data.Data = accentPtr;
            SetWindowCompositionAttribute(h, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            CenterWindow();
            base.OnRender(drawingContext);
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
                Thread.Sleep(10);
                WindowData.WindowItems[WindowData.SelectedIndex].Activate();
            }
            WindowManager.EndPreview();
            Hide();
        }

        public WindowsViewModel WindowData { get; set; } = new WindowsViewModel();

        protected override void OnActivated(EventArgs e)
        {
            DataContext = WindowData;
            var windowHelper = new WindowInteropHelper(this);
            var dpiInfo = VisualTreeHelper.GetDpi(this);

            WindowData.MaxItemHeight = dpiInfo.DpiScaleY * 300;
            CenterWindow();
            base.OnActivated(e);
        }

        private void CenterWindow()
        {
            var screenCenter = WindowData.CenterScreen;
            var dpiInfo = VisualTreeHelper.GetDpi(this);
            var scale = dpiInfo.DpiScaleX;
            MaxWidth = WindowData.CursorScreen.Bounds.Width * .6 * scale;
            MaxHeight = WindowData.CursorScreen.Bounds.Height * .6 * scale;

            Left = WindowData.CursorScreen.Bounds.Left * scale + (WindowData.CursorScreen.Bounds.Width / scale - ActualWidth) / 2;
            //Left = screenCenter.X / (scale*2);
            Top = WindowData.CursorScreen.Bounds.Top * scale + (WindowData.CursorScreen.Bounds.Height / scale - ActualHeight) / 2;
        }

        public void SelectWindow(int direction)
        {

            if (Visibility == Visibility.Visible)
            {
                CenterWindow();
                ChangeSelection(direction);
                return;
            }

            var currentApplication = WindowManager
                .GetCurrentApplication();

            if (currentApplication is null)
            {
                return;
            }

            var windows = currentApplication.GetWindows2().ToList();
            WindowData.SelectedIndex = -1;
            WindowData.WindowItems = windows
                .Select(w => new WindowItem(w))
                .ToArray()
                ?? Array.Empty<WindowItem>();
            if (windows.Count > 0)
            {
                WindowData.SelectedIndex = 0;
            }


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
            lv.Focus();
            FocusManager.SetIsFocusScope(lv, true);
            //var childCount = lv.Items.Count;
            //var containers = Enumerable.Range(0, childCount).Select(i => (lv.Items[i], lv.ItemContainerGenerator.ContainerFromIndex(i) as Visual)).ToArray();
            //var coords = containers.Select(container => (container.Item1, container.Item2, container.Item2?.TransformToVisual(lv).Transform(new Point(0, 0)))).ToArray();

            //var zipped = Enumerable.Range(0, childCount).Select(i => (lv.Items[i], containers[i], coords[i])).ToArray();
            ////FocusManager.
            //var currentCoords = coords[lv.SelectedIndex].Item3;
            ///lv.Focus();
            //e.Handled = true;
            if(!new Key[] { Key.Down, Key.Up, Key.Left, Key.Right}.Contains(e.SystemKey))
            {
                return;
            }
            var infos = new List<WindowTileInfo>(lv.Items.Count);
            for (int i = 0; i < lv.Items.Count; i++)
            {

                var tile = GetTile(i);

                //if(tile.Location.Y != selectedTile.Location.Y)
                //{
                //    continue;
                //}
                //var distance = (int)(dir * (tile.Location.X - selectedTile.Location.X));
                //if (distance < 0)
                //{
                //    distance *= lv.Items.Count * -100;
                //}
                //tile.Distance = distance;

                infos.Add(tile);
            }
            var tileGrid = WindowTileGrid.Create(infos);
            if (e.SystemKey == Key.Down)
            {
                tileGrid.MoveDown();
                var next = tileGrid.SelectedItem;

                lv.SelectedItem = next;
                //Dispatcher.Invoke(() => ChangeSelection(1));
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Up)
            {
                tileGrid.MoveUp();
                var next = tileGrid.SelectedItem;
                lv.SelectedItem = next;
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Left)
            {
                tileGrid.MoveLeft();
                var next = tileGrid.SelectedItem;
                lv.SelectedItem = next;
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Right)
            {
                tileGrid.MoveRight();
                var next = tileGrid.SelectedItem;
                lv.SelectedItem = next;
                e.Handled = true;
                //lv.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                //MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                ////e.Handled = true;

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

        public static (WindowTileInfo[][] grid, int SelectedX, int SelectedY) ToGrid(IEnumerable<WindowTileInfo> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            // Collect unique X and Y coordinates, sorted for consistent order
            var xs = items.Select(i => i.Location.X).Distinct().OrderBy(x => x).ToList();
            var ys = items.Select(i => i.Location.Y).Distinct().OrderBy(y => y).ToList();

            int cols = xs.Count;
            int rows = ys.Count;
            int selectedX = 0;
            int selectedY = 0;
            // Create a 2D array with [row, column] indexing
            var grid = new WindowTileInfo[rows][];
            for(int i = 0; i < rows; i++)
            {
                grid[i] = new WindowTileInfo[cols];
            }
            // Build lookup for coordinate to index
            var xIndex = xs.Select((x, i) => (x, i)).ToDictionary(t => t.x, t => t.i);
            var yIndex = ys.Select((y, i) => (y, i)).ToDictionary(t => t.y, t => t.i);

            // Place items into their appropriate cells
            foreach (var item in items)
            {
                int col = xIndex[item.Location.X];
                int row = yIndex[item.Location.Y];
                grid[row][col] = item;
                if (item.IsSelected)
                {
                    selectedX = col;
                    selectedY = row;
                }
            }

            for(int i = 0; i < rows; i++)
            {
                var realLength = Array.IndexOf(grid[i], null);
                if(realLength >= 0)
                {
                    Array.Resize(ref grid[i], realLength);
                    Debug.WriteLine($"row {i} length {grid[i].Length}");
                }
            }
            return (grid, selectedX, selectedY);
        }

        private object GetNearestX(int dir)
        {
            var lv = TabListView;
            //lv.Focus();
            //FocusManager.SetIsFocusScope(lv, true);
            Debug.WriteLine($"dir: {dir}");
            //var selectedTile = GetTile(WindowData.SelectedIndex);
            var infos = new List<WindowTileInfo>(lv.Items.Count);
            for (int i = 0; i < lv.Items.Count; i++)
            {

                var tile = GetTile(i);

                //if(tile.Location.Y != selectedTile.Location.Y)
                //{
                //    continue;
                //}
                //var distance = (int)(dir * (tile.Location.X - selectedTile.Location.X));
                //if (distance < 0)
                //{
                //    distance *= lv.Items.Count * -100;
                //}
                //tile.Distance = distance;

                infos.Add(tile);
            }

            foreach (var s in infos)
            {
                Debug.WriteLine(s.ToString());
            }

            var (grid, selectedX, selectedY) = ToGrid(infos);

            var selectedColumnLength = grid.Length;
            var selectedRowLength = grid[selectedY].Length;
            var nextX = selectedX;
            var nextY = selectedY;
            if (dir == 1)
            {
                Debug.WriteLine($"current row length = {selectedRowLength}");
                nextX = (nextX + 1) % selectedRowLength;
                if(nextX == 0)
                {
                    nextY = (nextY + 1) % selectedColumnLength;
                }
            }
            else
            {
                nextX -= 1;
                if (nextX < 0)
                {
                    nextX = selectedRowLength - 1;
                    nextY = (nextY - 1);
                    if(nextY  < 0)
                    {
                        nextY = 0;
                    }
                }
            }
            var nextItem = grid[nextY][nextX]?.WindowItem; 
            Debug.WriteLine($"Current {selectedX}, {selectedY}");
            Debug.WriteLine($"Next {nextX}, {nextY}");
            Debug.WriteLine($"Next item {nextItem}");
            return nextItem;



            //var childCount = lv.Items.Count;
            //var containers = Enumerable.Range(0, childCount).Select(i => (lv.Items[i], lv.ItemContainerGenerator.ContainerFromIndex(i) as Visual)).ToArray();
            //var coords = containers.Select(container => (container.Item1, container.Item2, container.Item2?.TransformToVisual(lv).Transform(new Point(0, 0)))).Where(x => x.Item3 != null).ToArray();

            //var zipped = Enumerable.Range(0, childCount).Select(i => (lv.Items[i], containers[i], coords[i])).ToArray();
            //var currentCoords = coords[lv.SelectedIndex].Item3;
            //var zz = coords.Where(other => other.Item3.Value.X != currentCoords.Value.X && other.Item3.Value.Y == currentCoords.Value.Y);
            //var sorted = zz.OrderBy(other => dir * (other.Item3.Value.X - currentCoords.Value.X));
            //foreach(var s in sorted)
            //{
            //    Debug.WriteLine("{4}: {0} - {1},{2} - score {3} ", ((WindowItem)s.Item1).Handle, s.Item3.Value.X, s.Item3.Value.Y, dir * (s.Item3.Value.X - currentCoords.Value.X), lv.Items.IndexOf(s.Item1));
            //}
            //var next = sorted.First();


            //return next.Item1;

        }
        private void TabListView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SwitchWindowAndClose();
        }

        private void TabListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                (e.Source as System.Windows.Controls.ListView)?.ScrollIntoView(e.AddedItems[0]);
            }
        }
    }
}