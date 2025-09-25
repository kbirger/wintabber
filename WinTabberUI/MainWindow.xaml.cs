using Gma.System.MouseKeyHook;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
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
        public KeySequenceEventSource Listener;
        private IKeyboardEventSource _hook;

        public WindowManager WindowManager { get; } = new(new InteropProxy());
        public MainWindow()
        {
            InitializeComponent();
            _hook = WindowsInput.Capture.Global.KeyboardAsync(true);
            _hook.KeyDown += Hook_KeyDown;
        }

        private void Hook_KeyDown(object? sender, EventSourceEventArgs<KeyDown> e)
        {
            if(e.Data.Key == KeyCode.Oem3 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Run();
            }
        }

        public WindowsViewModel  WindowData { get; set; } = new WindowsViewModel();

        protected override void OnActivated(EventArgs e)
        {
            DataContext = WindowData;
            Run();
            base.OnActivated(e);
        }
        public void Run()
        {
            WindowData.WindowItems = [];
            WindowData.WindowItems = WindowManager
                .GetCurrentApplication()?
                .GetWindows()
                .Select(w => new WindowItem(w))
                .ToArray() 
                ?? Array.Empty<WindowItem>();
        }

        private void MainWindow_MouseDown(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            Console.WriteLine("X");
        }
    }
}