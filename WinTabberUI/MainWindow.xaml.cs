using Gma.System.MouseKeyHook;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WindowsInput.Events;
using WindowsInput.Events.Sources;
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

            //_hook = WindowsInput.Capture.Global.KeyboardAsync(true);
            //_hook.KeyDown += Hook_KeyDown;
            var nextWindow = Combination.FromString("Alt+Oem3");
            var prevWindow = Combination.FromString("Shift+Alt+Oem3");
            Hook.GlobalEvents()
                .OnCombination(
                    new Dictionary<Combination, Action>() 
                    {
                        { nextWindow, () => Run(1) },
                        { prevWindow, () => Run(-1) }
                    });


            //Hook.GlobalEvents().KeyDown += MainWindow_KeyDown;
            Hook.GlobalEvents().KeyUp += MainWindow_KeyUp;
        }




        private void MainWindow_KeyUp(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == System.Windows.Forms.Keys.LMenu)
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

        private void MainWindow_KeyDown(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            //    if (Visibility == Visibility.Hidden && e.Control && e.Alt && e.KeyCode == System.Windows.Forms.Keys.Oem3)
            //    {
            //        //Activate();
            //        Run();
            //    }
            //    else if (Visibility == Visibility.Visible && e.Alt && e.KeyCode == Keys.Oem3)
            //    {
            //        WindowData.SelectedIndex++;
            //    }
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
            var hnd = new WindowInteropHelper(this).Handle;
            //e.AddedItems.OfType<WindowItem>().FirstOrDefault()?.WindowRef.Preview(hnd);
        }
    }
}