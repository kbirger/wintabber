using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using WinTabberUI.Windowing;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for DockWindow.xaml
    /// </summary>
    public partial class DockWindow : Window
    {
        public DockWindowViewModel _viewModel = new DockWindowViewModel();
        private System.Drawing.Rectangle? _rect;
        public DockWindow()
        {
            InitializeComponent();
            Resources.MergedDictionaries.Add(System.Windows.Application.Current.Resources);
            DataContext = _viewModel;
            Top = 0;
            Left = 0;
            //Top = Screen.PrimaryScreen.Bounds.Top / 2;
            //Left = Screen.PrimaryScreen.Bounds.Width - ActualWidth;
            IsVisibleChanged += DockWindow_IsVisibleChanged;
        }

        private void DockWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if(Visibility == Visibility.Visible)
            {
                if (_rect is null)
                {
                    _rect = Screen.PrimaryScreen?.WorkingArea;
                    DesktopHelper.SetDesktopArea(new Rect(_rect.Value.X + ActualWidth * 1.25, _rect.Value.Y, _rect.Value.Width - ActualWidth * 1.25, _rect.Value.Height));

                }
            }
        }

        protected override void OnActivated(EventArgs e)
        {            
            base.OnActivated(e);
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            Hide();
            if(_rect is not null)
            {
                DesktopHelper.SetDesktopArea(new Rect(0, _rect.Value.Y, _rect.Value.Width, _rect.Value.Height));
                _rect = null;
            }
            e.Cancel = true;
        }

        private DependencyProperty _applicationName = DependencyProperty.Register(
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
            get { return (string)GetValue(_applicationName); }
            set
            {
                SetValue(_applicationName, value);
            }
        }
    }
}
