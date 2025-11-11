using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class WindowSelectorViewCoordinator : ViewCoordinatorBase<MainWindow>
    {
        private ApplicationStateViewModel _vm;

        public WindowSelectorViewCoordinator(ApplicationStateViewModel vm, IServiceProvider provider)
            : base(provider)
        {
            ReuseInstances = true;
            _vm = vm;
        }
        protected override void Close(MainWindow instance)
        {
            instance.Hide();
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsSwitcherActiveChanges;
        }

        protected override void Show(MainWindow instance)
        {
            instance.ShowWindowSelector();
        }
    }
}
