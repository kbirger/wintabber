//using Microsoft.Extensions.DependencyInjection;
//using System.Linq.Expressions;
//using System.Reactive.Linq;
//using System.Windows;
//using WinTabber.Events;
//using WinTabberUI.Models;
//using WinTabberUI.ViewModels;

//namespace WinTabberUI.Coordinators;

//public class WinTabberWindowCoordinator : IDisposable
//{
//    protected enum CloseStateStrategy
//    {
//        Hide,
//        Close
//    }

//    protected interface IConfigurationItem
//    {
//        void Open(Window window);

//        Window GetService(IServiceProvider serviceProvider);
//        IObservable<bool> ChangeEvents { get; }
//        CloseStateStrategy Strategy { get; }
//        public EventType? CloseOnEvent { get; }

//        Type WindowType { get; }
//    }
//    protected class ConfigurationItem<T>(IObservable<bool> changeEvents, CloseStateStrategy strategy, Action<T>? openFunc = null, EventType? closeOnEvent = null) : IConfigurationItem where T : Window
//    {
//        public IObservable<bool> ChangeEvents { get; } = changeEvents;
//        public CloseStateStrategy Strategy { get; } = strategy;
//        public EventType? CloseOnEvent { get; } = closeOnEvent;

//        private Action<T>? _openFunc = openFunc;

//        public Window GetService(IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<T>();

//        public Type WindowType => typeof(T);

//        public void Open(Window window)
//        {
//            if (_openFunc != null && window is T tWindow)
//            {
//                _openFunc(tWindow);
//            }
//            else
//            {
//                window.Show();
//            }
//        }
//    }


//    public WinTabberWindowCoordinator(
//        ApplicationStateViewModel applicationState, 
//        WinTabberEventManager eventManager, 
//        IServiceProvider serviceProvider)
//    {
//        IConfigurationItem[] configuration = [
//            new ConfigurationItem<WindowSelectorWindow>(applicationState.IsSwitcherActiveChanges, CloseStateStrategy.Hide, (w) => w.ShowWindowSelector(), EventType.WindowSelected),
//            // new ConfigurationItem<DockWindow>(applicationState.IsDockActiveChanges, CloseStateStrategy.Close),
//            new ConfigurationItem<MediaControlsWindow>(applicationState.IsMediaControlsActiveChanges, CloseStateStrategy.Close),
//        ];


//        foreach(var config in configuration.Where(c => c.CloseOnEvent is not null))
//        {
//            eventManager.CommandEvents
//                .ObserveOnDispatcher()
//                .Where(evt => evt.Type == config.CloseOnEvent)
//                .Subscribe(evt => TryClose(config.WindowType, config.Strategy));
//        }

//        _configLookup = configuration.ToDictionary(
//            config => config.WindowType,
//            config => config.ChangeEvents
//                .ObserveOnDispatcher()
//                .Subscribe(state => SetState(state, config.WindowType, config.Strategy, config.GetService, config.Open))
            
//        );
//        _serviceProvider = serviceProvider;
//    }

//    protected Dictionary<Type, Window> _windows = new Dictionary<Type, Window>();

//    private void SetState(bool state, Type windowType, CloseStateStrategy strategy, Func<IServiceProvider, Window> getService, Action<Window> openFunc)
//    {
//        bool hasInstance = _windows.TryGetValue(windowType, out var windowInstance);

//        if (state)
//        {
//            windowInstance = Open(windowType, getService, openFunc, hasInstance, windowInstance);
//        }
//        else if (!state && hasInstance)
//        {
//            Close(windowType, strategy, windowInstance!);
//        }

//    }

//    private Window Open(Type windowType, Func<IServiceProvider, Window> getService, Action<Window> openFunc, bool hasInstance, Window? windowInstance)
//    {
//        if (!hasInstance)
//        {
//            windowInstance = getService(_serviceProvider);
//            _windows.Add(windowType, windowInstance);

//        }
//        openFunc(windowInstance);
//        return windowInstance;
//    }

//    private void TryClose(Type windowType, CloseStateStrategy strategy)
//    {
//        if(_windows.TryGetValue(windowType, out var windowInstance))
//        {
//            Close(windowType, strategy, windowInstance);
//        }
//    }

//    private void Close(Type windowType, CloseStateStrategy strategy, Window windowInstance)
//    {
//        if (strategy == CloseStateStrategy.Hide)
//        {
//            windowInstance!.Hide();
//        }
//        else
//        {
//            windowInstance!.Close();
//            _windows.Remove(windowType);
//        }
//    }

//    private readonly Dictionary<Type, IDisposable> _configLookup;
//    private readonly IServiceProvider _serviceProvider;

//    public void Dispose()
//    {
//        foreach (var item in _configLookup.Values)
//        {
//            item.Dispose();
//        }
//    }
//}
