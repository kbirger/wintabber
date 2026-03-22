using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace WinTabberUI.Infrastructure
{
    public class ViewLocator(IServiceProvider serviceProvider) : IViewLocator
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        public IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract = null) where TViewModel : class
        {
            var view = _serviceProvider.GetRequiredService<IViewFor<TViewModel>>();
            return view;
        }

        public IViewFor? ResolveView(object? instance, string? contract = null)
        {
            if(instance is null)
            {
                return null;
            }
            var instanceType = instance.GetType();
            Type[] types = [instanceType, ..instanceType.GetInterfaces()];

            return types
                .Select(type =>
                    _serviceProvider
                        .GetService(typeof(IViewFor<>)
                        .MakeGenericType(type)))
                .FirstOrDefault(view => view is not null) as IViewFor;
        }
    }
}
