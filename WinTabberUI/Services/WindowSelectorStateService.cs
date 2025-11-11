using AutomaticInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WinTabber.Events;
using WinTabberUI.Extensions;

namespace WinTabberUI.Services
{
    [GenerateAutomaticInterface]
    public class WindowSelectorStateService(WinTabberEventManager manager)  :  IWindowSelectorStateService
    {
        private readonly WinTabberEventManager _manager = manager;

        public IObservable<bool> GetIsEditingChanges()
        {
            return _manager.CommandEvents
            .Where(evt => evt.Type == EventType.EditingStateChanged)
            .OfType<WinTabberEvent<bool>>()
            .Select(evt => evt.Arg)
            .StartWith(false)
            .Replay(1)
            .RefCount()
            .ObserveOnDispatcher();
        }

        public IObservable<bool> GetWindowChanges()
        {
            return _manager.CommandEvents
            .Where(evt => evt.Type.IsOneOf(EventType.CmdNextWindow, EventType.CmdPreviousWindow, EventType.CmdAppHide, EventType.WindowSelected))
            .WithLatestFrom<WinTabberEvent, bool, (WinTabberEvent CommandEvent, bool IsEditing)>(GetIsEditingChanges(), (command, isEditing) => (command, isEditing))
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
        }
    }
}
