using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Timers;
using System.Windows;
using WinTabber.API;
using WinTabber.Interop;

namespace WinTabberUI
{
    public class DockWindowViewModel : DependencyObject
    {

        public DockWindowViewModel()
        {
            
        }


        private WindowManager _windowManager = new WindowManager(new InteropProxy());
        private ApplicationRef? _application;

        private ApplicationRef Application => _application ?? throw new InvalidOperationException("No active application");
        private DependencyProperty _applicationName = DependencyProperty.Register(
            "ApplicationName",
            typeof(string),
            typeof(DockWindowViewModel),
            new PropertyMetadata(null, OnApplicationNameChanged));

        private static void OnApplicationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if(d is DockWindowViewModel vm && e.NewValue is string newApplicationName)
            {
                vm.UpdateApplication(newApplicationName);
            }
        }

        private void UpdateApplication(string newApplicationName)
        {
            _application = new ApplicationRef(newApplicationName, _windowManager);
            RefreshWindows();
        }

        private void RefreshWindows()
        {
            if(_application is null)
            {
                return;
            }
            var windows = _application.GetWindows();
            Windows.Clear();
            foreach (var window in windows)
            {
                Windows.Add(new WindowItem(window));
            }
        }

        public string ApplicationName
        {
            get { return (string)GetValue(_applicationName); }
            set
            {
                SetValue(_applicationName, value);
            }
        }

        private DependencyProperty _windows = DependencyProperty.Register(
            "Windows",
            typeof(ObservableCollection<WindowItem>),
            typeof(DockWindowViewModel),
            new PropertyMetadata(new ObservableCollection<WindowItem>()));
        
        private readonly System.Timers.Timer _timer;

        public ObservableCollection<WindowItem> Windows
        {
            get { return (ObservableCollection<WindowItem>)GetValue(_windows); }
            set
            {
                SetValue(_windows, value);
            }
        }

        public WindowRef[] GetMaximizedWindows()
        {
            return _application
                ?.GetWindows()
                .Where(w => w.State == WindowPlacement.WindowState.Maximized)
                .ToArray() ?? [];
        }
    }
}
