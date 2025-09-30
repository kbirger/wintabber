using GlobalHotKeys;
using GlobalHotKeys.Native.Types;
using Gma.System.MouseKeyHook;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using WinTabber.API;
using WinTabber.Interop;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public WindowManager WindowManager { get; } = new(new InteropProxy());

        public MainWindow()
        {
            InitializeComponent();


            var hotKeyManager = new HotKeyManager();
            var nextWindow = new HotKey(0, Modifiers.Alt, VirtualKeyCode.VK_OEM_3);
            var prevWindow = new HotKey(1, Modifiers.Alt | Modifiers.Shift, VirtualKeyCode.VK_OEM_3);
            var nextWindowReg = hotKeyManager.Register(nextWindow.Key, nextWindow.Modifiers);
            var prevWindowReg = hotKeyManager.Register(prevWindow.Key, prevWindow.Modifiers);
            hotKeyManager.HotKeyPressed.Subscribe(e =>
            {
                if (e.Equals(nextWindow))
                {
                    Dispatcher.Invoke(() => Run(1));
                }
                else if (e.Equals(prevWindow))
                {
                    Dispatcher.Invoke(() => Run(-1));
                }
            });

            Hook.GlobalEvents().KeyUp += MainWindow_KeyUp;
        }

        private void MainWindow_KeyUp(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Keys.LMenu)
            {
                if (Visibility == Visibility.Visible && WindowData.SelectedIndex >= 0 && WindowData.SelectedIndex < WindowData.WindowItems.Length)
                {
                    Thread.Sleep(10);
                    WindowData.WindowItems[WindowData.SelectedIndex].Activate();
                }
                WindowManager.EndPreview();
                Hide();
            }
        }

        public WindowsViewModel WindowData { get; set; } = new WindowsViewModel();

        protected override void OnActivated(EventArgs e)
        {
            DataContext = WindowData;

            base.OnActivated(e);
        }
        public void Run(int direction)
        {

            if (Visibility == Visibility.Visible)
            {
                WindowData.SelectedIndex += direction;
                return;
            }

            var currentApplication = WindowManager
                .GetCurrentApplication();

            if(currentApplication is null)
            {
                return;
            }

            var windows = currentApplication.GetWindows().ToList();
            //var currentWindow = currentApplication.CurrentWindow();
            //var currentIndex = windows.IndexOf(currentWindow!);
            WindowData.WindowItems = windows
                .Select(w => new WindowItem(w))
                .ToArray()
                ?? Array.Empty<WindowItem>();
            WindowData.SelectedIndex = 0;


            Show();
            Focus();
            TabListView.Focus();
            Title = NativeMethods.GetForegroundWindow().ToString();
        }

        private void TabListView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.SystemKey == Key.Down)
            {
                WindowData.SelectedIndex++;
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Up)
            {
                WindowData.SelectedIndex--;
                e.Handled = true;
            }
        }

        private void TabListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //var hnd = NativeMethods.GetForegroundWindow();
            //var hnd = new WindowInteropHelper(this).Handle;

            //if(e.AddedItems.Count > 0)
            //{
            //    var h = e.AddedItems.OfType<WindowItem>().FirstOrDefault()?.WindowRef.Handle;
            //    Thumb.SetSourceWindow((nint)h);

            //}
        }
    }
}