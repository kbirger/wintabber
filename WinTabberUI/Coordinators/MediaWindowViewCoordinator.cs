using WinTabberUI.Helpers;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class MediaWindowViewCoordinator : ViewCoordinatorBase<MediaControlsWindow>
    {
        private ApplicationStateViewModel _vm;
        private readonly CompositionWindowAnimator _animator = new();

        public MediaWindowViewCoordinator(ApplicationStateViewModel vm, IServiceProvider provider)
            : base(provider)
        {
            ReuseInstances = true;
            _vm = vm;
        }

        protected override void Close(MediaControlsWindow instance)
        {
            _animator.AnimateHide(instance, 150);
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsMediaControlsActiveChanges;
        }

        protected override void Show(MediaControlsWindow instance)
        {
            //instance.Show();
            _animator.AnimateShow(instance, 200);
        }
    }
}
