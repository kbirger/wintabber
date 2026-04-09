using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinTabberUI.Chrome
{
    /// <summary>
    /// Interaction logic for CaptionButtons.xaml
    /// </summary>
    public partial class CaptionButtons : UserControl
    {
        public CaptionButtons()
        {
            InitializeComponent();
        }

        public required Window Window { get; set; }
        private void CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {

        }

        private void CanMaximize(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Window?.WindowState != WindowState.Maximized;
        }


        private void CanRestore(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void CanClose(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void CanMinimize(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Window?.WindowState != WindowState.Minimized;

        }

        private void Restore(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.RestoreWindow(Window);
        }

        private void Maximize(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.MaximizeWindow(Window);
        }

        private void Close(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.CloseWindow(Window);
        }

        private void Minimize(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(Window);
        }
    }
}
