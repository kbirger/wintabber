using System.Reactive.Linq;
using WinTabber.API;
using WinTabberUI.Models;

namespace WinTabberUI.Updaters
{
    public sealed class WindowHistoryUpdater : IDisposable
    {
        private IDisposable? _subscription;
        private readonly ApplicationStateMonitor _appState;
        private readonly WindowManager _windowManager;

        public WindowHistoryUpdater(ApplicationStateMonitor appState, WindowManager windowManager)
        {
            _appState = appState;
            // todo: move state from windowManager to a viewmodel
            _windowManager = windowManager;
        }

        public void Init()
        {
            _subscription = _appState.ActiveWindowChanges
                .Where(window => window is not null)
            .Subscribe(window => _windowManager.RegisterForegroundWindowChanged(window!.Handle));
        }
        public void Dispose()
        {
            _subscription?.Dispose();
        }

    }
}
