using System.Configuration;
using System.Data;
using System.Windows;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IKeyboardEventSource _hook;

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
