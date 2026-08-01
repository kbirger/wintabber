using System.Reactive.Linq;
using WinTabber.API.Suspension;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class SuspendedWindowsViewCoordinator : ViewCoordinatorBase<SuspendedWindowsWindow>
    {
        private readonly WindowSelectorViewModel _selectorViewModel;
        private readonly IProcessSuspensionService _suspensionService;

        public SuspendedWindowsViewCoordinator(
            WindowSelectorViewModel selectorViewModel,
            IProcessSuspensionService suspensionService,
            IServiceProvider provider)
            : base(provider)
        {
            ReuseInstances = true;
            _selectorViewModel = selectorViewModel;
            _suspensionService = suspensionService;
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _selectorViewModel.IsSwitcherActiveChanges
                .CombineLatest(_suspensionService.HasSuspendedChanges, (active, has) => active && has)
                .DistinctUntilChanged();
        }

        protected override void Show(SuspendedWindowsWindow instance)
        {
            instance.Show();
        }

        protected override void Close(SuspendedWindowsWindow instance)
        {
            instance.Hide();
        }
    }
}
