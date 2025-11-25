using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class MediaWindowViewCoordinator : ViewCoordinatorBase<MediaControlsWindow>
    {
        private ApplicationStateViewModel _vm;

        public MediaWindowViewCoordinator(ApplicationStateViewModel vm, IServiceProvider provider)
            : base(provider)
        {
            _vm = vm;
        }
        protected override void Close(MediaControlsWindow instance)
        {
            instance.Close();
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsMediaControlsActiveChanges;
        }

        protected override void Show(MediaControlsWindow instance)
        {
            instance.Show();
        }
    }
}
