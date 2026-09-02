using System.Reactive.Linq;
using WinTabber.API.Suspension;
using WinTabber.Events;
using WinTabberUI.Models.Settings;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Coordinators
{
    public class SuspendedWindowsViewCoordinator : ViewCoordinatorBase<SuspendedWindowsWindow>
    {
        private readonly WindowSelectorViewModel _selectorViewModel;
        private readonly IProcessSuspensionService _suspensionService;
        private readonly WinTabberEventManager _eventManager;
        private readonly GeneralSettings _settings;

        public SuspendedWindowsViewCoordinator(
            WindowSelectorViewModel selectorViewModel,
            IProcessSuspensionService suspensionService,
            WinTabberEventManager eventManager,
            ApplicationSettings settings,
            IServiceProvider provider)
            : base(provider)
        {
            ReuseInstances = true;
            _selectorViewModel = selectorViewModel;
            _suspensionService = suspensionService;
            _eventManager = eventManager;
            _settings = settings.General;
        }

        protected override IObservable<bool> GetChangeEvents()
        {
            // Original behavior: show while the switcher is up and something is actually suspended.
            var followsSwitcher = _selectorViewModel
                .IsSwitcherActiveChanges.CombineLatest(
                    _suspensionService.HasSuspendedChanges,
                    (active, has) => active && has
                );

            // CmdSuspendedWindows pins the window open independently of the switcher; pressing it
            // again unpins. Seeded with StartWith(false) so the combined stream still emits when the
            // command is never used, leaving the behavior above untouched.
            var pinnedOpen = _eventManager
                .CommandEvents.Where(evt => evt.Type == EventType.CmdSuspendedWindows && _settings.EnableWindowSuspension)
                .Scan(false, (isPinned, _) => !isPinned)
                .StartWith(false);

            return followsSwitcher
                .CombineLatest(pinnedOpen, (visibleWithSwitcher, isPinned) => visibleWithSwitcher || isPinned)
                // Read on the next switcher open or pin, same as the per-tile sleep button: this
                // does not re-check mid-session, so a bar already open when the feature turns off
                // stays open until the next visibility change.
                .Select(isVisible => isVisible && _settings.EnableWindowSuspension)
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
