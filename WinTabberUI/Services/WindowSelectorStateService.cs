//using AutomaticInterface;
//using ReactiveUI;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reactive;
//using System.Reactive.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows;
//using WinTabber.Events;
//using WinTabberUI.Extensions;
//using WinTabberUI.ViewModels;

//namespace WinTabberUI.Services
//{
//    public partial class WindowSelectorStateService(WinTabberEventManager manager, WindowSelectorViewModel windowSelectorViewModel) : IWindowSelectorStateService
//    {
//        private readonly WinTabberEventManager _manager = manager;
//        private readonly WindowSelectorViewModel _windowSelectorViewModel = windowSelectorViewModel;

     

//        [Lazy]
//        private IObservable<bool> GetWindowSelectorChanges()
//        {
//            return _manager.CommandEvents
//                .SubscribeOn(RxApp.TaskpoolScheduler)
//                .Where(evt => evt.Type.IsOneOf(EventType.CmdNextWindow, EventType.CmdPreviousWindow, EventType.CmdAppHide, EventType.WindowSelected))
//                .WithLatestFrom<WinTabberEvent, bool, (WinTabberEvent CommandEvent, bool IsEditing)>(_windowSelectorViewModel.IsEditing, (command, isEditing) => (command, isEditing))
//                .Select(evt =>
//                {
//                    var command = evt.CommandEvent;
//                    var isEditing = evt.IsEditing;
//                    return command.Type switch
//                    {
//                        EventType.CmdNextWindow => true,
//                        EventType.CmdPreviousWindow => true,
//                        EventType.WindowSelected => false,
//                        EventType.CmdAppHide => isEditing,
//                        _ => throw new InvalidOperationException()
//                    };
//                })
//                .StartWith(false)
//                .DistinctUntilChanged()
//                .Replay(1)
//                .RefCount()
//                .ObserveOnDispatcher();
//        }
//    }
//}
