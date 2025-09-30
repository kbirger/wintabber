using System.Configuration;
using System.Data;
using System.Windows;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

        }

        protected override void OnExit(ExitEventArgs e)
        {
            //_hook?.Dispose();
        }
    }

}
