using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class WindowSelectorViewCoordinator : ViewCoordinatorBase<WindowSelectorWindow>
    {
        private WindowSelectorViewModel _vm;

        public WindowSelectorViewCoordinator(WindowSelectorViewModel vm, IServiceProvider provider)
            : base(provider)
        {
            ReuseInstances = true;
            _vm = vm;
        }
        protected override void Close(WindowSelectorWindow instance)
        {
            instance.Hide();
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsSwitcherActiveChanges;
        }

        protected override void Show(WindowSelectorWindow instance)
        {
            instance.ShowWindowSelector();
        }
    }
}
