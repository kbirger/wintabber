using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Linq;
using System.Windows;
using WinTabberUI.Models;

namespace WinTabberUI.Coordinators
{
    public class WinTabberWindowCoordinator : IDisposable
    {
        protected enum StateStrategy
        {
            Hide,
            Close
        }

        protected interface IConfigurationItem
        {
            Window GetService(IServiceProvider serviceProvider);
            IObservable<bool> ChangeEvents { get; }
            StateStrategy Strategy { get; }

            Type WindowType { get; }
        }
        protected class ConfigurationItem<T>(IObservable<bool> changeEvents, StateStrategy strategy) : IConfigurationItem where T : Window
        {
            public IObservable<bool> ChangeEvents { get; } = changeEvents;
            public StateStrategy Strategy { get; } = strategy;

            public Window GetService(IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<T>();

            public Type WindowType => typeof(T);
        }


        public WinTabberWindowCoordinator(EventMonitor applicationState, IServiceProvider serviceProvider)
        {
            IConfigurationItem[] configuration = [
                new ConfigurationItem<MainWindow>(applicationState.IsSwitcherActiveChanges, StateStrategy.Hide),
                new ConfigurationItem<DockWindow>(applicationState.IsDockActiveChanges, StateStrategy.Close),
                new ConfigurationItem<MediaControlsWindow>(applicationState.IsMediaControlsActiveChanges, StateStrategy.Close),
            ];

            _configLookup = configuration.ToDictionary(
                config => config.WindowType,
                config => config.ChangeEvents
                    .ObserveOnDispatcher()
                    .Subscribe(state => SetState(state, config.WindowType, config.Strategy, config.GetService))
                
            );
            _serviceProvider = serviceProvider;
        }

        protected Dictionary<Type, Window> _windows = new Dictionary<Type, Window>();

        private void SetState(bool state, Type windowType, StateStrategy strategy, Func<IServiceProvider, Window> getService)
        {
            bool hasInstance = _windows.TryGetValue(windowType, out var windowInstance);

            if (state && !hasInstance)
            {
                var instance = getService(_serviceProvider);
                _windows.Add(windowType, instance);
                instance.Show();
            }
            else if (!state && hasInstance)
            {
                if (strategy == StateStrategy.Hide)
                {
                    windowInstance!.Hide();
                }
                else
                {
                    windowInstance!.Close();
                    _windows.Remove(windowType);
                }
            }

        }


        private readonly Dictionary<Type, IDisposable> _configLookup;
        private readonly IServiceProvider _serviceProvider;

        public void Dispose()
        {
            foreach (var item in _configLookup.Values)
            {
                item.Dispose();
            }
        }
    }
}
