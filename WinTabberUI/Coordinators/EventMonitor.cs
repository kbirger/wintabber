using System.Diagnostics;
using System.Reactive.Linq;
using WinTabber.API;
using WinTabber.Events;

namespace WinTabberUI.Models
{
    public partial class EventMonitor 
    {
        private readonly WindowManager _windowManager;
        private readonly ApplicationState _applicationState;
        private readonly WinTabberEventManagerThreadHost _eventManager;

        public EventMonitor(WindowManager windowManager, ApplicationState applicationState)
        {
            _windowManager = windowManager;
            _applicationState = applicationState;
            _eventManager = WinTabberEventManagerThreadHost.Instance;

            var commandEvents = _eventManager.CommandEvents.Publish().RefCount();
            ActiveWindowChanges = _eventManager.WindowChange
                .Replay(1)
                .RefCount()
                .Select(data => _windowManager.GetWindow(data.Arg))
                .Where(windowRef => windowRef is null || windowRef.IsValidUserWindow && windowRef.Process.IsValid)
                .ObserveOnDispatcher();

            ActiveApplicationChanges = _eventManager.ApplicationChange
                .Replay(1)
                .RefCount()
                .Select(data => _windowManager.GetApplication(data.Arg))
                .Where(applicationRef => applicationRef is null || applicationRef.IsValidProcess)
                .ObserveOnDispatcher();

            IsSwitcherActiveChanges = commandEvents
                .Where(evt => EventOneOf(evt.Type, EventType.NextWindow, EventType.PreviousWindow, EventType.AppHide))
                .Select(evt => evt.Type switch
                {
                    EventType.NextWindow => true,
                    EventType.PreviousWindow => true,
                    EventType.AppHide => false,
                    _ => throw new InvalidOperationException()
                })
                .StartWith(false)
                .DistinctUntilChanged()
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();

            IsDockActiveChanges = commandEvents
                .Where(evt => evt.Type == EventType.DockWindow)
                .Scan(false, (current, _) => !current)
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();


            IsMediaControlsActiveChanges = commandEvents
                .Where(evt => evt.Type == EventType.MediaWindow)
                .Scan(false, (current, _) => !current)
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();

            //_activeApplication = ActiveApplicationChanges.ToProperty(this, m => m.ActiveApplication);
            //_activeWindow = ActiveWindowChanges.ToProperty(this, m => m.ActiveWindow);
            //_isSwitcherActive = IsSwitcherActiveChanges.ToProperty(this, m => m.IsSwitcherActive);
            //_isSwitcherActive = IsDockActiveChanges.ToProperty(this, m => m.IsDockActive);
            //_isSwitcherActive = IsMediaControlsActiveChanges.ToProperty(this, m => m.IsMediaControlsActive);
            
            ActiveWindowChanges.Subscribe(window =>
            {
                Debug.WriteLine($"({Thread.CurrentThread.ManagedThreadId}) Window changed: {window?.Handle} - {window?.Title}");
                _applicationState.ActiveWindow = window;
            });

            ActiveApplicationChanges.Subscribe(application =>
            {
                Debug.WriteLine($"({Thread.CurrentThread.ManagedThreadId}) Application changed: {application?.ProcessName}");
                _applicationState.ActiveApplication = application;
            });

            IsSwitcherActiveChanges.Subscribe(value =>
            {
                Debug.WriteLine($"({Thread.CurrentThread.ManagedThreadId}) IsSwitcherActiveChanges changed: {value}");
                _applicationState.IsWindowSelectorActive = value;
            });

            IsDockActiveChanges.Subscribe(value =>
            {
                Debug.WriteLine($"({Thread.CurrentThread.ManagedThreadId}) IsDockActiveChanges changed: {value}");
                _applicationState.IsDockWindowActive = value;
            });

            IsMediaControlsActiveChanges.Subscribe(value =>
            {
                Debug.WriteLine($"({Thread.CurrentThread.ManagedThreadId}) IsMediaControlsActiveChanges changed: {value}");
                _applicationState.IsMediaControlWindowActive = value;
            });
        }

        private static bool EventOneOf(EventType type, params EventType[] types)
        {
            return types.Contains(type);
        }
        public IObservable<WindowRef?> ActiveWindowChanges { get; private set; }
        public IObservable<ApplicationRef?> ActiveApplicationChanges { get; private set; }

        public IObservable<bool> IsSwitcherActiveChanges { get; private set; }

        public IObservable<bool> IsDockActiveChanges { get; private set; }

        public IObservable<bool> IsMediaControlsActiveChanges { get; private set; }
    }
}
