using WinTabberUI.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI.Coordinators
{
    public class SettingsWindowViewCoordinator : ViewCoordinatorBase<SettingsWindow>
    {
        private SettingsViewModel _vm;

        public SettingsWindowViewCoordinator(SettingsViewModel vm, IServiceProvider provider)
            : base(provider)
        {
            ReuseInstances = false;
            _vm = vm;
        }
        protected override void Close(SettingsWindow instance)
        {
            instance.Close();
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsSettingsShown;
        }

        protected override void Show(SettingsWindow instance)
        {
            instance.Closed += Instance_Closed;
            instance.Show();
        }

        private void Instance_Closed(object? sender, EventArgs e)
        {
            if(sender is SettingsWindow window)
            {
                window.Closed -= Instance_Closed;
                OnExternallyClosed();
                _vm.Hide();                
            }
        }
    }
}
