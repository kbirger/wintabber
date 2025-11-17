using System.Reactive.Linq;
using WinTabber.API;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Updaters
{
    public sealed class WindowHistoryUpdater : IDisposable
    {
        private IDisposable? _subscription;
        private readonly ApplicationStateViewModel _appState;
        private readonly WindowManager _windowManager;

        public WindowHistoryUpdater(ApplicationStateViewModel appState, WindowManager windowManager)
        {
            _appState = appState;
            // todo: move state from windowManager to a viewmodel
            _windowManager = windowManager;
        }

        public IDisposable Init()
        {
            _subscription = _appState.ActiveWindowChanges
                .Where(window => window is not null)
            .Subscribe(window => _windowManager.RegisterForegroundWindowChanged(window!.Handle));

            return this;
        }
        public void Dispose()
        {
            _subscription?.Dispose();
        }

    }
}
