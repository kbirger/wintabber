using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using WinTabber.Interop;
using WinTabberUI.Helpers;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class MediaWindowViewCoordinator : ViewCoordinatorBase<MediaControlsWindow>
    {
        private ApplicationStateViewModel _vm;
        private readonly CompositionWindowAnimator _animator = new();

        // AnimateHide keeps the window visible for the length of the animation. A hotkey press
        // during that time must show the window again, so the coordinator must not read
        // Window.IsVisible here.
        private bool _isShown;

        private readonly IInteropProxy _interop;

        public MediaWindowViewCoordinator(
            ApplicationStateViewModel vm,
            IInteropProxy interop,
            IServiceProvider provider
        )
            : base(provider)
        {
            ReuseInstances = true;
            _vm = vm;
            _interop = interop;
        }

        // Set this to false to show and hide the window without the animation. The last diagnostic
        // round proved that the animation is not the cause of the hide problem.
        private static readonly bool UseAnimation = true;

        protected override bool IsInstanceShown(MediaControlsWindow instance) => _isShown;

        protected override void Close(MediaControlsWindow instance)
        {
            _isShown = false;
            Debug.WriteLine("media controls: coordinator hides the window");
            if (UseAnimation)
            {
                _animator.AnimateHide(instance, 150);
            }
            else
            {
                instance.Hide();
            }
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            return _vm.IsMediaControlsActiveChanges;
        }

        protected override void Show(MediaControlsWindow instance)
        {
            _isShown = true;
            Debug.WriteLine("media controls: coordinator shows the window");
            if (UseAnimation)
            {
                _animator.AnimateShow(instance, 200);
            }
            else
            {
                instance.Show();
            }

            // The hotkey arrives through a global hook, so this process holds no foreground right.
            // Window.Show() then puts the window on top without activation, and the window never
            // gets the deactivation that hides it again. ForceForeground attaches to the input
            // queue of the foreground thread to get around the restriction.
            nint handle = new WindowInteropHelper(instance).Handle;
            if (handle == nint.Zero)
            {
                Debug.WriteLine("media controls: no window handle, cannot force the foreground");
                return;
            }

            Debug.WriteLine(
                $"media controls: forcing foreground for {handle}, foreground before = {_interop.GetForegroundWindowHandle()}"
            );
            _interop.ForceForeground((int)handle);
            Debug.WriteLine($"media controls: foreground after = {_interop.GetForegroundWindowHandle()}");

            // WPF activates a window the first time it is shown. A later Show() of a hidden window
            // does not. ForceForeground then moves the foreground of the system, but WPF still
            // treats the window as inactive, so it takes no keyboard focus and raises no
            // Deactivated event. Activate() repairs that, once the show has reached the queue.
            instance.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () =>
                {
                    if (!instance.IsActive)
                    {
                        Debug.WriteLine("media controls: window is not active after the show - activating");
                        instance.Activate();
                    }

                    Debug.WriteLine(
                        $"media controls: after the show, active={instance.IsActive}, "
                            + $"foreground={_interop.GetForegroundWindowHandle()}"
                    );
                }
            );
        }
    }
}
