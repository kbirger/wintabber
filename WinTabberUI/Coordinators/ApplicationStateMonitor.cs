using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using ReactiveUI;
using WinTabber.API;
using WinTabber.Events;

namespace WinTabberUI.Models
{
    public partial class ApplicationStateMonitor : ReactiveObject
    {
        private readonly WindowManager _windowManager;
        private readonly WinTabberEventManager _eventManager;

        public ApplicationStateMonitor(WindowManager windowManager, WinTabberEventManager eventManager)
        {
            _windowManager = windowManager;
            _eventManager = eventManager;

            var commandEvents = _eventManager.CommandEvents.SubscribeOn(RxApp.TaskpoolScheduler);

            ActiveWindowChanges = _eventManager.WindowChange
                .Select(data => _windowManager.GetWindow(data.Arg))
                .Where(windowRef => windowRef is null || windowRef.IsValidUserWindow && windowRef.Process.IsValid)
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();

            ActiveApplicationChanges = _eventManager.ApplicationChange
                .Select(data => _windowManager.GetApplication(data.Arg))
                .Where(applicationRef => applicationRef is null || applicationRef.IsValidProcess)
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();

            IsSwitcherActiveChanges = commandEvents
                .Where(evt => EventOneOf(evt.Type, EventType.CmdNextWindow, EventType.CmdPreviousWindow, EventType.CmdAppHide))
                .Select(evt =>
                {
                    return evt.Type switch
                    {
                        EventType.CmdNextWindow => true,
                        EventType.CmdPreviousWindow => true,
                        EventType.CmdAppHide => false,
                        _ => throw new InvalidOperationException()
                    };
                })
                .StartWith(false)
                .DistinctUntilChanged()
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();

            IsDockActiveChanges = commandEvents
                .Where(evt => evt.Type == EventType.CmdDockWindow)
                .Scan(false, (current, _) => !current)
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();


            IsMediaControlsActiveChanges = commandEvents
                .Where(evt => evt.Type == EventType.CmdMediaWindow)
                .Scan(false, (current, _) => !current)
                .Replay(1)
                .RefCount()
                .ObserveOnDispatcher();

            _activeApplication = ActiveApplicationChanges.ToProperty(this, m => m.ActiveApplication);
            _activeWindow = ActiveWindowChanges.ToProperty(this, m => m.ActiveWindow);
            _isSwitcherActive = IsSwitcherActiveChanges.ToProperty(this, m => m.IsSwitcherActive);
            _isSwitcherActive = IsDockActiveChanges.ToProperty(this, m => m.IsDockActive);
            _isSwitcherActive = IsMediaControlsActiveChanges.ToProperty(this, m => m.IsMediaControlsActive);
            
            ActiveWindowChanges.Subscribe(w =>
            {
                Debug.WriteLine($"Window changed: {w.Handle} - {w.Title}");
            });

            ActiveApplicationChanges.Subscribe(p =>
            {
                Debug.WriteLine($"Application changed: {p.ProcessName}");
            });

            IsSwitcherActiveChanges.Subscribe(t =>
            {
                Debug.WriteLine($"IsSwitcherActiveChanges changed: {t}");
            });

            IsDockActiveChanges.Subscribe(t =>
            {
                Debug.WriteLine($"IsDockActiveChanges changed: {t}");
            });

            IsMediaControlsActiveChanges.Subscribe(t =>
            {
                Debug.WriteLine($"IsMediaControlsActiveChanges changed: {t}");
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

        private readonly ObservableAsPropertyHelper<WindowRef?> _activeWindow;
        private readonly ObservableAsPropertyHelper<ApplicationRef?> _activeApplication;
        private readonly ObservableAsPropertyHelper<bool> _isSwitcherActive;
        private readonly ObservableAsPropertyHelper<bool> _isDockActive;
        private readonly ObservableAsPropertyHelper<bool> _isMediaControlsActive;

        public WindowRef? ActiveWindow => _activeWindow.Value;
        public ApplicationRef? ActiveApplication => _activeApplication.Value;
        public bool IsSwitcherActive => _isSwitcherActive.Value;
        public bool IsDockActive => _isDockActive.Value;
        public bool IsMediaControlsActive => _isMediaControlsActive.Value;
    }
}
