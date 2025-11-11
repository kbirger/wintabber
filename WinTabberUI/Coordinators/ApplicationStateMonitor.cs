using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using ReactiveUI;
using WinTabber.API;
using WinTabber.Events;
using WinTabberUI.Extensions;

namespace WinTabberUI.Models;

public partial class ApplicationStateMonitor : ReactiveObject
{
    private readonly WindowManager _windowManager;
    private readonly WinTabberEventManager _eventManager;

    public ApplicationStateMonitor(WindowManager windowManager, WinTabberEventManager eventManager)
    {
        _windowManager = windowManager;
        _eventManager = eventManager;

        var commandEvents = _eventManager.CommandEvents.SubscribeOn(RxApp.TaskpoolScheduler);

        IsEditingStateChanges = _eventManager.CommandEvents
            .Where(evt => evt.Type == EventType.EditingStateChanged)
            .OfType<WinTabberEvent<bool>>()
            .Select(evt => evt.Arg)
            .StartWith(false)
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();

        ActiveWindowChanges = _eventManager.WindowChange
            .Select(data => _windowManager.GetWindow(data.Arg))
            .Where(windowRef => windowRef is null || windowRef.IsValidUserWindow && windowRef.Process.IsValid)
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();

        ActiveApplicationChanges = _eventManager.ApplicationChange
            .Select(data => _windowManager.GetApplication(data.Arg))
            .Where(applicationRef => applicationRef is null || (applicationRef.IsValidProcess && applicationRef.CurrentWindow() is { }))
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();

        IsSwitcherActiveChanges = commandEvents
            .Where(evt => evt.Type.IsOneOf(EventType.CmdNextWindow, EventType.CmdPreviousWindow, EventType.CmdAppHide, EventType.WindowSelected))                
            .WithLatestFrom<WinTabberEvent, bool, (WinTabberEvent CommandEvent, bool IsEditing)>(IsEditingStateChanges, (command, isEditing) => (command, isEditing))
            .Select(evt =>
            {
                var command = evt.CommandEvent;
                var isEditing = evt.IsEditing;
                return command.Type switch
                {
                    EventType.CmdNextWindow => true,
                    EventType.CmdPreviousWindow => true,
                    EventType.WindowSelected => false,
                    EventType.CmdAppHide => isEditing,
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
            var x = p?.CurrentWindow();
            Debug.WriteLine($"Application changed: {p.ProcessName}; {p.CurrentWindow()?.Class}");
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

    public IObservable<bool> IsEditingStateChanges { get; }
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
