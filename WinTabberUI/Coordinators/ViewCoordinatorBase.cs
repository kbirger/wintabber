using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WinTabberUI.Coordinators
{
    public abstract class ViewCoordinatorBase<T> : IDisposable where T : Window
    {
        public ViewCoordinatorBase(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        private IDisposable _listener;

        private T? _instance;
        private IServiceProvider _serviceProvider;


        protected bool ReuseInstances { get; init; } = false;
        protected abstract IObservable<bool> GetChangeEvents();

        private T GetInstance()
        {
            return _instance ??= CreateInstance();
        }
        protected virtual T CreateInstance()
        {
            return _serviceProvider.GetRequiredService<T>();
        }
        protected virtual void Release() { }

        protected virtual void OnInit() { }

        protected abstract void Close(T instance);

        protected abstract void Show(T instance);

        public IDisposable Init()
        {
            _listener = GetChangeEvents().ObserveOnDispatcher().Subscribe(OnEvent);
            return this;
        }

        [MemberNotNullWhen(true, nameof(_instance))]
        private bool IsShown => _instance?.IsVisible ?? false;

        private void OnEvent(bool value)
        {
            if (value)
            {
                ShowCore();
            }
            else
            {
                CloseCore();
            }
        }

        protected void OnExternallyClosed()
        {
            if (_instance is not null && !ReuseInstances)
            {
                _instance = null;
            }
        }

        private void CloseCore()
        {
            if (IsShown)
            {
                Close(_instance);

            }
            if (!ReuseInstances)
            {
                _instance = null;
            }
        }

        private void ShowCore()
        {
            if (!IsShown)
            {
                _instance = GetInstance();
                Show(_instance);
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
            Release();
        }
    }
}
