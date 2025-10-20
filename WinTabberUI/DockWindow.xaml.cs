using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using WinTabber.API;
using WinTabber.Interop;
using WinTabberUI.Windowing;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for DockWindow.xaml
    /// </summary>
    public partial class DockWindow : Window
    {
        public DockWindowViewModel _viewModel = new DockWindowViewModel();
        private WindowManager _windowManger = new WindowManager(new InteropProxy());
        private Rectangle? _rect;
        public DockWindow()
        {
            InitializeComponent();
            Resources.MergedDictionaries.Add(System.Windows.Application.Current.Resources);
            DataContext = _viewModel;
            _viewModel.ApplicationName = ApplicationName;
            Top = 0;
            Left = 0;
            //Top = Screen.PrimaryScreen.Bounds.Top / 2;
            //Left = Screen.PrimaryScreen.Bounds.Width - ActualWidth;
            IsVisibleChanged += DockWindow_IsVisibleChanged;
            LayoutUpdated += DockWindow_LayoutUpdated;
            Loaded += DockWindow_Loaded;
        }

        private void DockWindow_Loaded(object sender, RoutedEventArgs e)
        {
            MakeSpace();
        }

        private void DockWindow_LayoutUpdated(object? sender, EventArgs e)
        {
            //MakeSpace();
        }

        private void DockWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue.Equals(true))
            {
                //MakeSpace();
            }
        }

        private void MakeSpace()
        {
            if (_rect is null && ActualWidth > 0)
            {
                var dpiInfo = VisualTreeHelper.GetDpi(this);
                var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
                //var oldWorkingArea = DesktopHelper.GetDesktopArea();
                _rect = screen.WorkingArea;
                //if (oldWorkingArea is null)
                //{
                //    return;
                //}
                //_rect = oldWorkingArea;
                var newWorkingArea = new Rect(
                    screen.Bounds.Left + ActualWidth * dpiInfo.DpiScaleX, 
                    _rect.Value.Y , 
                    screen.Bounds.Width  - ActualWidth * dpiInfo.DpiScaleX,
                    _rect.Value.Height );
                DesktopHelper.SetDesktopArea(newWorkingArea);

                //Task.Run(() =>
                //{
                    var windows = _windowManger.GetWindows()
                        .Where(window => window.State != WindowPlacement.WindowState.Minimized && window.State != WindowPlacement.WindowState.Hidden && window.Bounds.X < newWorkingArea.X && window.Bounds.Width > 0);

                    foreach (var window in windows)
                    {
                        if (!window.Process.IsProcessElevated)
                        {
                            window.MoveTo(new System.Drawing.Point((int)newWorkingArea.X, window.Bounds.Y));

                        }
                    }
                //});
            }

        }

        protected override void OnActivated(EventArgs e)
        {            
            base.OnActivated(e);
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            if(_rect is not null)
            {
                var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
                var rect = new Rect(screen.Left, screen.Top, screen.Width, _rect.Value.Height);
                DesktopHelper.SetDesktopArea(rect);
                _rect = null;
            }
        }

        public static DependencyProperty ApplicationNameProperty = DependencyProperty.Register(
        "ApplicationName",
        typeof(string),
        typeof(DockWindow),
        new PropertyMetadata(null, OnApplicationNameChanged));

        private static void OnApplicationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DockWindow window && e.NewValue is string app)
                window._viewModel.ApplicationName = app;
        }

        public string ApplicationName
        {
            get => _viewModel.ApplicationName;
            set => _viewModel.ApplicationName = value;
        }
    }
}
