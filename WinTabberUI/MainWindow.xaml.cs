using GlobalHotKeys;
using GlobalHotKeys.Native.Types;
using Gma.System.MouseKeyHook;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using WindowsInput.Events;
using WinTabber.API;
using WinTabber.Interop;

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
        private readonly HotKey _hkNextWindow = new HotKey(0, Modifiers.Alt, VirtualKeyCode.VK_OEM_3);
        private readonly HotKey _hkPrevWindow = new HotKey(1, Modifiers.Alt | Modifiers.Shift, VirtualKeyCode.VK_OEM_3);
        private readonly MouseShortcut _hkMinPlain = new MouseShortcut(MouseButtons.Left, true, true, false, false);
        private readonly MouseShortcut _hkMaxPlain = new MouseShortcut(MouseButtons.Right, true, true, false, false);
        private readonly MouseShortcut _hkMin = new MouseShortcut(MouseButtons.XButton2, false, true, false, false);
        private readonly MouseShortcut _hkMax = new MouseShortcut(MouseButtons.XButton1, false, true, false, false);


        public MainWindow()
        {
            InitializeComponent();


            var hotKeyManager = new HotKeyManager();
            var nextWindowReg = hotKeyManager.Register(_hkNextWindow.Key, _hkNextWindow.Modifiers);
            var prevWindowReg = hotKeyManager.Register(_hkPrevWindow.Key, _hkPrevWindow.Modifiers);
            var keyHook = Hook.GlobalEvents();
            var mouseHook = WindowsInput.Capture.Global.Mouse();
            _resources.Add(hotKeyManager);
            _resources.Add(nextWindowReg);
            _resources.Add(prevWindowReg);
            _resources.Add(keyHook);
            _resources.Add(mouseHook);
            
            mouseHook.ButtonDown += (s, e) =>
            {
                var pressed = new MouseShortcut(e.Data.Button switch { 
                    ButtonCode.XButton1 => MouseButtons.XButton1,
                    ButtonCode.XButton2 => MouseButtons.XButton2,
                    ButtonCode.Left => MouseButtons.Left,
                    ButtonCode.Middle => MouseButtons.Middle,
                    ButtonCode.Right => MouseButtons.Right,
                    _ => MouseButtons.None
                },
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Windows));

                if (pressed.Equals(_hkMinPlain) || pressed.Equals(_hkMin))
                {
                    WindowManager.CurrentWindow()?.Minimize();
                }
                else if (pressed.Equals(_hkMaxPlain) || pressed.Equals(_hkMax))
                {
                    WindowManager.CurrentWindow()?.Maximize();
                }
            };

            hotKeyManager.HotKeyPressed.Subscribe(CycleWindows);
            keyHook.KeyUp += KeyHook_KeyUp;
            //keyHook.MouseDown += KeyHook_MouseDown;
        }

        private void KeyHook_MouseDown(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            var pressed = new MouseShortcut(e.Button,
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                                            Keyboard.Modifiers.HasFlag(ModifierKeys.Windows));

            if (pressed.Equals(_hkMinPlain) || pressed.Equals(_hkMin))
            {
                WindowManager.CurrentWindow()?.Minimize();
            }
            else if (pressed.Equals(_hkMaxPlain) || pressed.Equals(_hkMax))
            {
                WindowManager.CurrentWindow()?.Maximize();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            foreach(var disposable in _resources)
            {
                disposable.Dispose();
            }
            
            base.OnClosed(e);
        }

        private void CycleWindows(HotKey e)
        {
            if (e.Equals(_hkNextWindow))
            {
                Dispatcher.Invoke(() => Run(1));
            }
            else if (e.Equals(_hkPrevWindow))
            {
                Dispatcher.Invoke(() => Run(-1));
            }
        }

        private void KeyHook_KeyUp(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Keys.LMenu)
            {
                Dispatcher.Invoke(SwitchWindowAndClose);
            }
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

            base.OnActivated(e);
        }
        public void Run(int direction)
        {

            if (Visibility == Visibility.Visible)
            {
                ChangeSelection(direction);
                return;
            }

            var currentApplication = WindowManager
                .GetCurrentApplication();

            if(currentApplication is null)
            {
                return;
            }

            var windows = currentApplication.GetWindows().ToList();
            WindowData.WindowItems = windows
                .Select(w => new WindowItem(w))
                .ToArray()
                ?? Array.Empty<WindowItem>();
            WindowData.SelectedIndex = 0;


            Show();
            Focus();
            TabListView.Focus();
        }

        private void ChangeSelection(int direction)
        {
            WindowData.SelectedIndex += direction;
        }

        private void TabListView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.SystemKey == Key.Down)
            {
                Dispatcher.Invoke(() => ChangeSelection(1));
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Up)
            {
                Dispatcher.Invoke(() => ChangeSelection(-1));
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